using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BwDependencyScanner.Core;

public sealed class BwRepositoryScanner
{
    private static readonly string[] Extensions =
    [
        ".process", ".proc", ".xml", ".subprocess", ".sharedhttp", ".rvtransport", ".wsdl", ".xsd", ".aeschema", ".jdbc"
    ];

    public BwRepositoryIndex Scan(string rootPath)
    {
        var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => ParseFile(rootPath, f))
            .ToList();

        var edges = BuildEdges(files);

        return new BwRepositoryIndex
        {
            RootPath = rootPath,
            Files = files,
            Edges = edges
        };
    }

    public BwMessageDependencyReport BuildMessageReport(string rootPath, string messageName, string? folderFilter = null)
    {
        var index = Scan(rootPath);

        var directFiles = index.Files
            .Where(f =>
                (string.IsNullOrWhiteSpace(folderFilter) || f.RelativePath.Contains(folderFilter, StringComparison.OrdinalIgnoreCase)) &&
                f.MessageReferences.Any(m => m.Name.Equals(messageName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var reachable = Traverse(index, directFiles);
        var reachableFiles = index.Files
            .Where(f => reachable.Contains(f.RelativePath, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var variableValues = BuildVariableValueMap(reachableFiles);
        var resourceMap = BuildResourceConnectionMap(reachableFiles);

        var backendConnections = reachableFiles
            .SelectMany(f => f.BackendConnections)
            .Select(c => ResolveConnection(c, resourceMap, variableValues))
            .GroupBy(ConnectionFingerprint, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Kind.ToString())
            .ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BwMessageDependencyReport
        {
            MessageName = messageName,
            DirectFiles = directFiles,
            TransitiveProcesses = reachableFiles
                .Where(f => f.ResourceKind == "Process")
                .Select(f => f.RelativePath)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            GlobalVariables = reachableFiles
                .SelectMany(f => f.Variables.Where(v => v.Kind == BwVariableKind.Global).Select(v => v.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SharedVariables = reachableFiles
                .SelectMany(f => f.Variables.Where(v => v.Kind == BwVariableKind.Shared).Select(v => v.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            JobSharedVariables = reachableFiles
                .SelectMany(f => f.Variables.Where(v => v.Kind == BwVariableKind.JobShared).Select(v => v.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SharedResources = reachableFiles
                .SelectMany(f => f.SharedResourceReferences)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TraversedEdges = index.Edges
                .Where(e => reachable.Contains(e.SourceFile, StringComparer.OrdinalIgnoreCase))
                .ToList(),
            BackendConnections = backendConnections
        };
    }

    public string ExportCsv(BwMessageDependencyReport report)
    {
        var rows = new List<(string Category, string Value)>();
        rows.AddRange(report.TransitiveProcesses.Select(x => ("Process", Escape(x))));
        rows.AddRange(report.GlobalVariables.Select(x => ("GlobalVariable", Escape(x))));
        rows.AddRange(report.SharedVariables.Select(x => ("SharedVariable", Escape(x))));
        rows.AddRange(report.JobSharedVariables.Select(x => ("JobSharedVariable", Escape(x))));
        rows.AddRange(report.SharedResources.Select(x => ("SharedResource", Escape(x))));
        rows.AddRange(report.TraversedEdges.Select(x => ("Edge", Escape($"{x.SourceFile} -> {x.Target} ({x.DependencyType})"))));
        rows.AddRange(report.BackendConnections.Select(x => ("BackendConnection", Escape($"{x.Kind}|{x.SourceName}|{x.Url}|{x.Host}|{x.Port}|{x.Destination}"))));
        return string.Join(Environment.NewLine, rows.Select(r => $"{r.Category},\"{r.Value}\""));
    }

    public string ExportBackendConnectionsCsv(BwMessageDependencyReport report)
    {
        var rows = new List<string>
        {
            "BackendKind,SourceType,SourceName,SourcePath,ResourceRef,ActivityName,Url,Host,Port,PathOrOperation,Username,PasswordReference,DriverOrFactory,Destination,VariableReferences"
        };

        rows.AddRange(report.BackendConnections.Select(c =>
            string.Join(",",
                Quote(c.Kind.ToString()),
                Quote(c.SourceType),
                Quote(c.SourceName),
                Quote(c.SourcePath),
                Quote(c.ResourceRef),
                Quote(c.ActivityName),
                Quote(c.Url),
                Quote(c.Host),
                Quote(c.Port),
                Quote(c.PathOrOperation),
                Quote(c.Username),
                Quote(c.PasswordReference),
                Quote(c.DriverOrFactory),
                Quote(c.Destination),
                Quote(string.Join(" | ", c.VariableReferences)))));

        return string.Join(Environment.NewLine, rows);
    }

    private static string Quote(string value) => $"\"{Escape(value)}\"";
    private static string Escape(string value) => (value ?? string.Empty).Replace("\"", "\"\"");

    private static HashSet<string> Traverse(BwRepositoryIndex index, List<BwResourceFile> startFiles)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(startFiles.Select(f => f.RelativePath));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (var edge in index.Edges.Where(e => e.SourceFile.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                var targetFile = index.Files.FirstOrDefault(f =>
                    f.RelativePath.Equals(edge.Target, StringComparison.OrdinalIgnoreCase) ||
                    f.Name.Equals(edge.Target, StringComparison.OrdinalIgnoreCase) ||
                    f.RelativePath.EndsWith(edge.Target, StringComparison.OrdinalIgnoreCase));

                if (targetFile is not null && !visited.Contains(targetFile.RelativePath))
                    queue.Enqueue(targetFile.RelativePath);
            }
        }

        return visited;
    }

    private static List<BwDependencyEdge> BuildEdges(List<BwResourceFile> files)
    {
        var edges = new List<BwDependencyEdge>();

        foreach (var f in files)
        {
            edges.AddRange(f.ProcessReferences.Select(p => new BwDependencyEdge
            {
                SourceFile = f.RelativePath,
                SourceName = f.Name,
                Target = p,
                DependencyType = "Process"
            }));

            edges.AddRange(f.SharedResourceReferences.Select(r => new BwDependencyEdge
            {
                SourceFile = f.RelativePath,
                SourceName = f.Name,
                Target = r,
                DependencyType = "SharedResource"
            }));
        }

        return edges;
    }

    private static BwResourceFile ParseFile(string rootPath, string path)
    {
        var text = File.ReadAllText(path);
        var relative = Path.GetRelativePath(rootPath, path);
        var name = Path.GetFileNameWithoutExtension(path);
        var kind = InferKind(path, text);

        List<BwVariableUsage> variables;
        List<string> processRefs;
        List<string> sharedRefs;
        List<BwMessageReference> msgRefs;
        List<BwBackendConnection> backendConnections;

        try
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var root = doc.Root;
            if (root is not null)
                name = root.Attribute("name")?.Value ?? name;

            var nodes = root?.DescendantsAndSelf().ToList() ?? new List<XElement>();
            variables = ExtractVariables(nodes, text);
            processRefs = ExtractProcessRefs(nodes, text);
            sharedRefs = ExtractSharedResourceRefs(nodes, text);
            msgRefs = ExtractMessageRefs(nodes, text);
            backendConnections = ExtractBackendConnections(relative, kind, name, nodes, text, variables, sharedRefs);
        }
        catch
        {
            variables = ExtractVariables([], text);
            processRefs = ExtractProcessRefs([], text);
            sharedRefs = ExtractSharedResourceRefs([], text);
            msgRefs = ExtractMessageRefs([], text);
            backendConnections = ExtractBackendConnections(relative, kind, name, [], text, variables, sharedRefs);
        }

        return new BwResourceFile
        {
            Path = path,
            RelativePath = relative,
            ResourceKind = kind,
            Name = name,
            Variables = variables,
            ProcessReferences = processRefs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            SharedResourceReferences = sharedRefs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            MessageReferences = msgRefs
                .GroupBy(x => $"{x.Role}|{x.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BackendConnections = backendConnections
        };
    }

    private static string InferKind(string path, string text)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".process" or ".proc" or ".subprocess") return "Process";
        if (text.Contains("job shared variable", StringComparison.OrdinalIgnoreCase)) return "JobSharedVariableResource";
        if (text.Contains("shared variable", StringComparison.OrdinalIgnoreCase)) return "SharedVariableResource";
        if (text.Contains("global", StringComparison.OrdinalIgnoreCase) && text.Contains("variable", StringComparison.OrdinalIgnoreCase)) return "GlobalVariableResource";
        if (ext == ".wsdl") return "Wsdl";
        if (ext == ".xsd") return "Schema";
        return "XmlResource";
    }

    private static List<BwVariableUsage> ExtractVariables(IEnumerable<XElement> nodes, string text)
    {
        var vars = new List<BwVariableUsage>();

        vars.AddRange(Regex.Matches(text, "%[^%]+%")
            .Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.Global, Evidence = m.Value }));

        vars.AddRange(Regex.Matches(text, @"tibco\.clientVar\.[A-Za-z0-9_.-]+", RegexOptions.IgnoreCase)
            .Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.Global, Evidence = m.Value }));

        vars.AddRange(Regex.Matches(text, @"jobShared[A-Za-z0-9_.-]+", RegexOptions.IgnoreCase)
            .Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.JobShared, Evidence = m.Value }));

        vars.AddRange(Regex.Matches(text, @"sharedVar[A-Za-z0-9_.-]+", RegexOptions.IgnoreCase)
            .Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.Shared, Evidence = m.Value }));

        foreach (var node in nodes)
        {
            var value = node.Value.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (node.Name.LocalName.Contains("global", StringComparison.OrdinalIgnoreCase) &&
                node.Name.LocalName.Contains("variable", StringComparison.OrdinalIgnoreCase))
            {
                vars.Add(new BwVariableUsage { Name = value, Kind = BwVariableKind.Global, Evidence = node.Name.LocalName });
            }
            else if (node.Name.LocalName.Contains("shared", StringComparison.OrdinalIgnoreCase) &&
                     node.Name.LocalName.Contains("variable", StringComparison.OrdinalIgnoreCase))
            {
                vars.Add(new BwVariableUsage
                {
                    Name = value,
                    Kind = value.Contains("job", StringComparison.OrdinalIgnoreCase) ? BwVariableKind.JobShared : BwVariableKind.Shared,
                    Evidence = node.Name.LocalName
                });
            }
        }

        return vars
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .GroupBy(v => $"{v.Kind}|{v.Name}|{v.Evidence}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static List<string> ExtractProcessRefs(IEnumerable<XElement> nodes, string text)
    {
        var refs = new List<string>();

        refs.AddRange(nodes
            .Where(x => x.Name.LocalName.Equals("processName", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value.Trim()));

        refs.AddRange(nodes
            .Where(x => x.Name.LocalName.Equals("process", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value.Trim()));

        refs.AddRange(Regex.Matches(text, @"[A-Za-z0-9_./-]+\.process", RegexOptions.IgnoreCase)
            .Select(m => m.Value));

        return refs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> ExtractSharedResourceRefs(IEnumerable<XElement> nodes, string text)
    {
        var refs = new List<string>();

        refs.AddRange(nodes.SelectMany(x => x.Attributes()
            .Where(a => a.Name.LocalName.Contains("resource", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.LocalName.Contains("ref", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Value.Trim())));

        refs.AddRange(Regex.Matches(text, @"[A-Za-z0-9_./-]+\.(sharedhttp|rvtransport|wsdl|xsd|jdbc|aeschema)", RegexOptions.IgnoreCase)
            .Select(m => m.Value));

        return refs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<BwMessageReference> ExtractMessageRefs(IEnumerable<XElement> nodes, string text)
    {
        var refs = new List<BwMessageReference>();

        foreach (var node in nodes)
        {
            var local = node.Name.LocalName;
            var value = node.Value.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (local.Contains("input", StringComparison.OrdinalIgnoreCase) && local.Contains("message", StringComparison.OrdinalIgnoreCase))
                refs.Add(new BwMessageReference { Name = value, Role = BwMessageRole.Input, Evidence = local });
            else if (local.Contains("output", StringComparison.OrdinalIgnoreCase) && local.Contains("message", StringComparison.OrdinalIgnoreCase))
                refs.Add(new BwMessageReference { Name = value, Role = BwMessageRole.Output, Evidence = local });
            else if (local.Contains("fault", StringComparison.OrdinalIgnoreCase))
                refs.Add(new BwMessageReference { Name = value, Role = BwMessageRole.Fault, Evidence = local });
            else if (local.Contains("message", StringComparison.OrdinalIgnoreCase))
                refs.Add(new BwMessageReference { Name = value, Role = BwMessageRole.Unknown, Evidence = local });
            else if (local.Contains("operation", StringComparison.OrdinalIgnoreCase))
                refs.Add(new BwMessageReference { Name = value, Role = BwMessageRole.Operation, Evidence = local });
        }

        refs.AddRange(Regex.Matches(text, @"message\s*=\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)
            .Select(m => new BwMessageReference { Name = m.Groups[1].Value, Role = BwMessageRole.Unknown, Evidence = "regexMessage" }));

        return refs.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
    }

    private static List<BwBackendConnection> ExtractBackendConnections(
        string relativePath,
        string kind,
        string sourceName,
        IEnumerable<XElement> nodes,
        string text,
        List<BwVariableUsage> variables,
        List<string> sharedRefs)
    {
        var allNodes = nodes.ToList();

        string FindNodeValue(params string[] names) =>
            allNodes.FirstOrDefault(x => names.Any(n => x.Name.LocalName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                ?.Value?.Trim() ?? string.Empty;

        string FindAttrValue(params string[] names) =>
            allNodes.SelectMany(x => x.Attributes())
                .FirstOrDefault(a => names.Any(n => a.Name.LocalName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                ?.Value?.Trim() ?? string.Empty;

        var activityName = FindNodeValue("activityName", "activity", "name");
        var url = FirstNonEmpty(
            FindNodeValue("url", "endpoint", "service"),
            FindAttrValue("url", "endpoint", "service"));
        var host = FirstNonEmpty(
            FindNodeValue("host", "server", "machine"),
            FindAttrValue("host", "server", "machine"));
        var port = FirstNonEmpty(FindNodeValue("port"), FindAttrValue("port"));
        var user = FirstNonEmpty(
            FindNodeValue("user", "username", "login"),
            FindAttrValue("user", "username", "login"));
        var passwordRef = FirstNonEmpty(
            FindNodeValue("password", "credential", "secret"),
            FindAttrValue("password", "credential", "secret"));
        var operation = FirstNonEmpty(
            FindNodeValue("operation", "path", "serviceOperation"),
            FindAttrValue("operation", "path", "serviceOperation"));
        var destination = FirstNonEmpty(
            FindNodeValue("queue", "topic", "subject", "destination"),
            FindAttrValue("queue", "topic", "subject", "destination"));
        var driverOrFactory = FirstNonEmpty(
            FindNodeValue("driver", "connectionFactory", "factory"),
            FindAttrValue("driver", "connectionFactory", "factory"));
        var jdbcUrl = Regex.Match(text, @"jdbc:[^\""""'<>\s]+", RegexOptions.IgnoreCase).Value;

        var backendKind = InferBackendKind(relativePath, text, jdbcUrl, url, destination);
        if (backendKind == BwBackendKind.Unknown)
            return [];

        var variableRefs = variables
            .Select(v => v.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return
        [
            new BwBackendConnection
            {
                Kind = backendKind,
                SourceType = kind,
                SourceName = sourceName,
                SourcePath = relativePath,
                ResourceRef = sharedRefs.FirstOrDefault() ?? string.Empty,
                ActivityName = activityName,
                Url = string.IsNullOrWhiteSpace(jdbcUrl) ? url : jdbcUrl,
                Host = host,
                Port = port,
                PathOrOperation = operation,
                Username = user,
                PasswordReference = passwordRef,
                DriverOrFactory = driverOrFactory,
                Destination = destination,
                VariableReferences = variableRefs,
                ResolvedValues = BuildResolvedValues(relativePath, kind, sourceName, variableRefs)
            }
        ];
    }

    private static List<BwResolvedValue> BuildResolvedValues(string sourcePath, string sourceType, string sourceName, List<string> variableRefs) =>
        variableRefs.Select(v => new BwResolvedValue
        {
            Name = v,
            Value = string.Empty,
            SourceType = sourceType,
            SourceName = sourceName,
            SourcePath = sourcePath,
            Evidence = "detected-variable-reference"
        }).ToList();

    private static BwBackendKind InferBackendKind(string relativePath, string text, string jdbcUrl, string url, string destination)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();

        if (ext == ".jdbc" || !string.IsNullOrWhiteSpace(jdbcUrl))
            return BwBackendKind.Jdbc;

        if (ext == ".sharedhttp" || !string.IsNullOrWhiteSpace(url))
            return text.Contains("soap", StringComparison.OrdinalIgnoreCase) || text.Contains("wsdl", StringComparison.OrdinalIgnoreCase)
                ? BwBackendKind.Soap
                : BwBackendKind.Http;

        if (ext == ".rvtransport")
            return BwBackendKind.Rv;

        if (!string.IsNullOrWhiteSpace(destination))
            return BwBackendKind.Jms;

        if (text.Contains("sftp", StringComparison.OrdinalIgnoreCase))
            return BwBackendKind.Sftp;

        if (text.Contains("ftp", StringComparison.OrdinalIgnoreCase))
            return BwBackendKind.Ftp;

        if (text.Contains("file", StringComparison.OrdinalIgnoreCase) && text.Contains("directory", StringComparison.OrdinalIgnoreCase))
            return BwBackendKind.File;

        return BwBackendKind.Unknown;
    }

    private static Dictionary<string, string> BuildVariableValueMap(List<BwResourceFile> files)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Where(f => f.ResourceKind.Contains("Variable", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var variable in file.Variables)
            {
                if (!map.ContainsKey(variable.Name))
                    map[variable.Name] = variable.Evidence;
            }
        }

        return map;
    }

    private static Dictionary<string, BwBackendConnection> BuildResourceConnectionMap(List<BwResourceFile> files)
    {
        var map = new Dictionary<string, BwBackendConnection>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            foreach (var connection in file.BackendConnections)
            {
                if (!string.IsNullOrWhiteSpace(file.RelativePath) && !map.ContainsKey(file.RelativePath))
                    map[file.RelativePath] = connection;

                if (!string.IsNullOrWhiteSpace(file.Name) && !map.ContainsKey(file.Name))
                    map[file.Name] = connection;
            }
        }

        return map;
    }

    private static BwBackendConnection ResolveConnection(
        BwBackendConnection connection,
        Dictionary<string, BwBackendConnection> resourceMap,
        Dictionary<string, string> variableValues)
    {
        var merged = connection;

        if (!string.IsNullOrWhiteSpace(connection.ResourceRef) &&
            resourceMap.TryGetValue(connection.ResourceRef, out var resourceConnection))
        {
            merged = Merge(connection, resourceConnection);
        }

        var resolvedValues = merged.VariableReferences
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => new BwResolvedValue
            {
                Name = v,
                Value = variableValues.TryGetValue(v, out var resolved) ? resolved : string.Empty,
                SourceType = merged.SourceType,
                SourceName = merged.SourceName,
                SourcePath = merged.SourcePath,
                Evidence = variableValues.ContainsKey(v) ? "resolved-from-variable-map" : "unresolved"
            })
            .ToList();

        return new BwBackendConnection
        {
            Kind = merged.Kind,
            SourceType = merged.SourceType,
            SourceName = merged.SourceName,
            SourcePath = merged.SourcePath,
            ResourceRef = merged.ResourceRef,
            ActivityName = merged.ActivityName,
            Url = ReplaceVariables(merged.Url, variableValues),
            Host = ReplaceVariables(merged.Host, variableValues),
            Port = ReplaceVariables(merged.Port, variableValues),
            PathOrOperation = ReplaceVariables(merged.PathOrOperation, variableValues),
            Username = ReplaceVariables(merged.Username, variableValues),
            PasswordReference = merged.PasswordReference,
            DriverOrFactory = ReplaceVariables(merged.DriverOrFactory, variableValues),
            Destination = ReplaceVariables(merged.Destination, variableValues),
            VariableReferences = merged.VariableReferences.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ResolvedValues = resolvedValues
        };
    }

    private static BwBackendConnection Merge(BwBackendConnection left, BwBackendConnection right) => new()
    {
        Kind = left.Kind != BwBackendKind.Unknown ? left.Kind : right.Kind,
        SourceType = left.SourceType,
        SourceName = left.SourceName,
        SourcePath = left.SourcePath,
        ResourceRef = FirstNonEmpty(left.ResourceRef, right.ResourceRef),
        ActivityName = FirstNonEmpty(left.ActivityName, right.ActivityName),
        Url = FirstNonEmpty(left.Url, right.Url),
        Host = FirstNonEmpty(left.Host, right.Host),
        Port = FirstNonEmpty(left.Port, right.Port),
        PathOrOperation = FirstNonEmpty(left.PathOrOperation, right.PathOrOperation),
        Username = FirstNonEmpty(left.Username, right.Username),
        PasswordReference = FirstNonEmpty(left.PasswordReference, right.PasswordReference),
        DriverOrFactory = FirstNonEmpty(left.DriverOrFactory, right.DriverOrFactory),
        Destination = FirstNonEmpty(left.Destination, right.Destination),
        VariableReferences = left.VariableReferences.Concat(right.VariableReferences).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        ResolvedValues = left.ResolvedValues.Concat(right.ResolvedValues).ToList()
    };

    private static string ReplaceVariables(string value, Dictionary<string, string> variableValues)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var result = value;
        foreach (var entry in variableValues)
        {
            result = result.Replace(entry.Key, entry.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string ConnectionFingerprint(BwBackendConnection c) =>
        string.Join("|",
            c.Kind,
            c.SourcePath ?? string.Empty,
            c.ResourceRef ?? string.Empty,
            c.Url ?? string.Empty,
            c.Host ?? string.Empty,
            c.Port ?? string.Empty,
            c.Destination ?? string.Empty,
            c.Username ?? string.Empty);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
