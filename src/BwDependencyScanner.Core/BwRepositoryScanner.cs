using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BwDependencyScanner.Core;

public sealed class BwRepositoryScanner
{
    private static readonly string[] Extensions = [".process", ".proc", ".xml", ".subprocess", ".sharedhttp", ".rvtransport", ".wsdl", ".xsd", ".aeschema", ".jdbc"];

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
            .Where(f => (string.IsNullOrWhiteSpace(folderFilter) || f.RelativePath.Contains(folderFilter, StringComparison.OrdinalIgnoreCase))
                && f.MessageReferences.Any(m => m.Name.Equals(messageName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var reachable = Traverse(index, directFiles);
        var reachableFiles = index.Files.Where(f => reachable.Contains(f.RelativePath, StringComparer.OrdinalIgnoreCase)).ToList();

        return new BwMessageDependencyReport
        {
            MessageName = messageName,
            DirectFiles = directFiles,
            TransitiveProcesses = reachableFiles.Where(f => f.ResourceKind == "Process").Select(f => f.RelativePath).OrderBy(x => x).ToList(),
            GlobalVariables = reachableFiles.SelectMany(f => f.Variables).Where(v => v.Kind == BwVariableKind.Global).Select(v => v.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            SharedVariables = reachableFiles.SelectMany(f => f.Variables).Where(v => v.Kind == BwVariableKind.Shared).Select(v => v.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            JobSharedVariables = reachableFiles.SelectMany(f => f.Variables).Where(v => v.Kind == BwVariableKind.JobShared).Select(v => v.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            SharedResources = reachableFiles.SelectMany(f => f.SharedResourceReferences).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            TraversedEdges = index.Edges.Where(e => reachable.Contains(e.SourceFile, StringComparer.OrdinalIgnoreCase)).ToList()
        };
    }

    public string ExportCsv(BwMessageDependencyReport report)
    {
        var rows = new List<string> { "Category,Value" };
        rows.AddRange(report.TransitiveProcesses.Select(x => $"Process,\"{Escape(x)}\""));
        rows.AddRange(report.GlobalVariables.Select(x => $"GlobalVariable,\"{Escape(x)}\""));
        rows.AddRange(report.SharedVariables.Select(x => $"SharedVariable,\"{Escape(x)}\""));
        rows.AddRange(report.JobSharedVariables.Select(x => $"JobSharedVariable,\"{Escape(x)}\""));
        rows.AddRange(report.SharedResources.Select(x => $"SharedResource,\"{Escape(x)}\""));
        rows.AddRange(report.TraversedEdges.Select(x => $"Edge,\"{Escape(x.SourceFile)} -> {Escape(x.Target)} [{Escape(x.DependencyType)}]\""));
        return string.Join(Environment.NewLine, rows);
    }

    private static string Escape(string value) => value.Replace("\"", "\"\"");

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
                    f.RelativePath.Equals(edge.Target, StringComparison.OrdinalIgnoreCase)
                    || f.Name.Equals(edge.Target, StringComparison.OrdinalIgnoreCase)
                    || f.RelativePath.EndsWith(edge.Target, StringComparison.OrdinalIgnoreCase));
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
        List<BwVariableUsage> variables = [];
        List<string> processRefs = [];
        List<string> sharedRefs = [];
        List<BwMessageReference> msgRefs = [];

        try
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var root = doc.Root;
            if (root != null)
            {
                name = root.Attribute("name")?.Value ?? name;
                var nodes = root.DescendantsAndSelf().ToList();
                variables = ExtractVariables(nodes, text);
                processRefs = ExtractProcessRefs(nodes, text);
                sharedRefs = ExtractSharedResourceRefs(nodes, text);
                msgRefs = ExtractMessageRefs(nodes, text);
            }
        }
        catch
        {
            variables = ExtractVariables([], text);
            processRefs = ExtractProcessRefs([], text);
            sharedRefs = ExtractSharedResourceRefs([], text);
            msgRefs = ExtractMessageRefs([], text);
        }

        return new BwResourceFile
        {
            Path = path,
            RelativePath = relative,
            ResourceKind = kind,
            Name = name,
            Variables = variables,
            ProcessReferences = processRefs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            SharedResourceReferences = sharedRefs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
            MessageReferences = msgRefs.GroupBy(x => $"{x.Role}:{x.Name}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name).ToList()
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
        vars.AddRange(Regex.Matches(text, @"%%[^%]+%%").Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.Global, Evidence = m.Value }));
        vars.AddRange(Regex.Matches(text, @"tibco\.clientVar\.[A-Za-z0-9_\.]+").Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.Global, Evidence = m.Value }));
        vars.AddRange(Regex.Matches(text, @"jobShared[A-Za-z0-9_\.]*", RegexOptions.IgnoreCase).Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.JobShared, Evidence = m.Value }));
        vars.AddRange(Regex.Matches(text, @"sharedVar[A-Za-z0-9_\.]*", RegexOptions.IgnoreCase).Select(m => new BwVariableUsage { Name = m.Value, Kind = BwVariableKind.Shared, Evidence = m.Value }));

        foreach (var node in nodes)
        {
            var value = node.Value.Trim();
            if (node.Name.LocalName.Contains("global", StringComparison.OrdinalIgnoreCase) && node.Name.LocalName.Contains("variable", StringComparison.OrdinalIgnoreCase))
                vars.Add(new BwVariableUsage { Name = value, Kind = BwVariableKind.Global, Evidence = node.Name.LocalName });
            if (node.Name.LocalName.Contains("shared", StringComparison.OrdinalIgnoreCase) && node.Name.LocalName.Contains("variable", StringComparison.OrdinalIgnoreCase))
                vars.Add(new BwVariableUsage { Name = value, Kind = value.Contains("job", StringComparison.OrdinalIgnoreCase) ? BwVariableKind.JobShared : BwVariableKind.Shared, Evidence = node.Name.LocalName });
        }

        return vars.Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
    }

    private static List<string> ExtractProcessRefs(IEnumerable<XElement> nodes, string text)
    {
        var refs = new List<string>();
        refs.AddRange(nodes.Where(x => x.Name.LocalName.Equals("processName", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value.Trim()));
        refs.AddRange(nodes.Where(x => x.Name.LocalName.Equals("process", StringComparison.OrdinalIgnoreCase)).Select(x => x.Value.Trim()));
        refs.AddRange(Regex.Matches(text, @"[A-Za-z0-9_\-/]+\.process").Select(m => m.Value));
        return refs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static List<string> ExtractSharedResourceRefs(IEnumerable<XElement> nodes, string text)
    {
        var refs = new List<string>();
        refs.AddRange(nodes.SelectMany(x => x.Attributes()).Where(a => a.Name.LocalName.Contains("resource", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName.Contains("ref", StringComparison.OrdinalIgnoreCase)).Select(a => a.Value.Trim()));
        refs.AddRange(Regex.Matches(text, @"[A-Za-z0-9_\-/]+\.(sharedhttp|rvtransport|wsdl|xsd|jdbc|aeschema)").Select(m => m.Value));
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

        refs.AddRange(Regex.Matches(text, @">([A-Za-z0-9_\-:/\.]+Message)<").Select(m => new BwMessageReference { Name = m.Groups[1].Value, Role = BwMessageRole.Unknown, Evidence = "regex:Message" }));
        return refs.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
    }
}
