using System.Text.RegularExpressions;
using System.Xml.Linq;
using ProcessAst.Core;

namespace ProcessAst.Bw;

public sealed class BwRepositoryParser
{
    private static readonly string[] Extensions = [".process", ".subprocess", ".sharedhttp", ".jdbc", ".rvtransport", ".wsdl", ".xsd", ".xml"];
    private readonly XPathXsltNormalizer _normalizer = new();

    public ProcessRepositoryAst ParseRepository(string rootFolder)
    {
        var repo = new ProcessRepositoryAst { Name = "Repository", Kind = ProcessNodeKind.Repository, Source = new SourceLocation { FilePath = rootFolder } };
        var files = Directory.EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories).Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();
        var processIndex = new Dictionary<string, ProcessDefinitionAst>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Where(IsProcessFile))
        {
            var process = ParseProcess(file);
            ResolveTransitionEndpoints(process);
            NormalizeGlobalVariableReferences(process, repo.GlobalVariables);
            repo.Processes.Add(process);
            repo.Children.Add(process);
            processIndex[Path.GetFileNameWithoutExtension(file)] = process;
        }

        foreach (var file in files.Where(f => !IsProcessFile(f)))
        {
            var text = File.ReadAllText(file);
            var r = new ResourceDefinitionAst { Name = Path.GetFileNameWithoutExtension(file), Kind = ProcessNodeKind.Resource, ResourceType = Path.GetExtension(file).TrimStart('.').ToLowerInvariant(), Address = ExtractAddress(text), Source = new SourceLocation { FilePath = file } };
            foreach (Match m in Regex.Matches(text, "message\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)) r.RelatedMessageNames.Add(m.Groups[1].Value);
            repo.Resources.Add(r);
            repo.Children.Add(r);
        }

        DiscoverGlobals(files, repo);
        foreach (var p in repo.Processes) NormalizeGlobalVariableReferences(p, repo.GlobalVariables);
        DiscoverMessages(repo);
        ResolveSubProcesses(repo, processIndex);
        RelateResources(repo);
        GenerateIntegrationFacades(repo);
        return repo;
    }

    private ProcessDefinitionAst ParseProcess(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var process = new ProcessDefinitionAst { Name = Path.GetFileNameWithoutExtension(path), Kind = ext == ".subprocess" ? ProcessNodeKind.SubProcess : ProcessNodeKind.Process, IsSubProcess = ext == ".subprocess", Source = new SourceLocation { FilePath = path } };
        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);
        process.Metadata["bwVersion"] = DetectVersion(doc);

        foreach (var n in doc.Descendants())
        {
            var ln = n.Name.LocalName;
            if (ln.Contains("starter", StringComparison.OrdinalIgnoreCase) || ln.Contains("receivehttp", StringComparison.OrdinalIgnoreCase) || ln.Contains("processstarter", StringComparison.OrdinalIgnoreCase)) process.InitialMessageName ??= n.Attribute("messageName")?.Value ?? n.Attribute("message")?.Value;
            if (ln.Contains("variable", StringComparison.OrdinalIgnoreCase)) process.LocalVariables.Add(new VariableDefinitionAst { Name = n.Attribute("name")?.Value ?? "var", Kind = ProcessNodeKind.Variable, Scope = VariableScope.Local, DataType = n.Attribute("type")?.Value ?? "string", Source = Line(path, n) });
            if (ln.Contains("activity", StringComparison.OrdinalIgnoreCase))
            {
                var type = n.Attribute("type")?.Value ?? ln;
                var a = new ActivityDefinitionAst { Name = n.Attribute("name")?.Value ?? ln, Kind = ProcessNodeKind.Activity, ActivityType = type, SemanticKind = InferActivity(type, ln), Source = Line(path, n) };
                if (type.Contains("http", StringComparison.OrdinalIgnoreCase)) a.Inputs["url"] = n.Attribute("url")?.Value ?? n.Attribute("endpoint")?.Value ?? "https://example.org";
                process.Activities.Add(a);
                if (type.Contains("CallProcess", StringComparison.OrdinalIgnoreCase))
                {
                    var target = n.Attribute("processName")?.Value ?? n.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("processName", StringComparison.OrdinalIgnoreCase))?.Value;
                    if (!string.IsNullOrWhiteSpace(target)) process.CalledSubProcessIds.Add(target);
                }
            }
            if (ln.Contains("transition", StringComparison.OrdinalIgnoreCase)) process.Transitions.Add(new TransitionDefinitionAst { Name = n.Attribute("label")?.Value ?? "transition", Kind = ProcessNodeKind.Transition, FromActivityId = n.Attribute("from")?.Value ?? n.Attribute("source")?.Value ?? "", ToActivityId = n.Attribute("to")?.Value ?? n.Attribute("target")?.Value ?? "", ConditionExpression = n.Attribute("condition")?.Value ?? n.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("condition", StringComparison.OrdinalIgnoreCase))?.Value, SemanticKind = InferTransition(n.Attribute("conditionType")?.Value), Source = Line(path, n) });
            if (ln.Contains("mapping", StringComparison.OrdinalIgnoreCase) || ln.Contains("mapper", StringComparison.OrdinalIgnoreCase) || ln.Contains("assign", StringComparison.OrdinalIgnoreCase))
            {
                var m = new MappingDefinitionAst { Name = n.Attribute("name")?.Value ?? "mapping", Kind = ProcessNodeKind.Mapping, OwnerProcessId = process.Id, Source = Line(path, n) };
                foreach (var e in n.Descendants())
                {
                    var el = e.Name.LocalName.ToLowerInvariant();
                    if (!(el.Contains("map") || el.Contains("assign") || el.Contains("copy") || el.Contains("value-of"))) continue;
                    var source = e.Attribute("from")?.Value ?? e.Attribute("source")?.Value ?? e.Attribute("select")?.Value ?? e.Value;
                    var norm = _normalizer.Normalize(source);
                    m.Entries.Add(new MappingEntryAst { TargetPath = e.Attribute("to")?.Value ?? e.Attribute("target")?.Value ?? e.Attribute("name")?.Value ?? el, SourceExpression = source, NormalizedExpression = norm.normalized, ExpressionKind = norm.kind, Transform = e.Attribute("function")?.Value ?? e.Attribute("transform")?.Value });
                }
                if (m.Entries.Count > 0) process.Mappings.Add(m);
            }
        }
        return process;
    }

    private static void ResolveTransitionEndpoints(ProcessDefinitionAst process)
    {
        var byName = process.Activities.ToDictionary(a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var t in process.Transitions)
        {
            if (byName.TryGetValue(t.FromActivityId, out var fromId)) t.FromActivityId = fromId;
            if (byName.TryGetValue(t.ToActivityId, out var toId)) t.ToActivityId = toId;
        }
    }

    private static void NormalizeGlobalVariableReferences(ProcessDefinitionAst process, IList<VariableDefinitionAst> globals)
    {
        var names = new HashSet<string>(globals.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in process.Mappings)
        foreach (var entry in mapping.Entries)
        {
            var text = entry.NormalizedExpression ?? entry.SourceExpression;
            var match = Regex.Match(text, @"\$?GlobalVariables[/:\.]([A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            var name = match.Groups[1].Value;
            entry.NormalizedExpression = $"global::{name}";
            if (!names.Contains(name)) globals.Add(new VariableDefinitionAst { Name = name, Kind = ProcessNodeKind.Variable, Scope = VariableScope.Global, DataType = "string" });
        }
    }

    private static void DiscoverGlobals(IEnumerable<string> files, ProcessRepositoryAst repo)
    {
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, "GlobalVariables?[:/\\.]([A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase))
            {
                var name = m.Groups[1].Value;
                if (repo.GlobalVariables.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                repo.GlobalVariables.Add(new VariableDefinitionAst { Name = name, Kind = ProcessNodeKind.Variable, Scope = VariableScope.Global, DataType = "string", Source = new SourceLocation { FilePath = file } });
            }
        }
    }

    private static void DiscoverMessages(ProcessRepositoryAst repo)
    {
        foreach (var process in repo.Processes.Where(p => !string.IsNullOrWhiteSpace(p.InitialMessageName)))
        {
            var msg = repo.RootMessages.FirstOrDefault(m => string.Equals(m.Name, process.InitialMessageName, StringComparison.OrdinalIgnoreCase));
            if (msg == null)
            {
                msg = new MessageDefinitionAst { Name = process.InitialMessageName!, Kind = ProcessNodeKind.Message, Source = process.Source };
                repo.RootMessages.Add(msg);
                repo.Children.Add(msg);
            }
            msg.EntryProcessIds.Add(process.Id);
        }
    }

    private static void ResolveSubProcesses(ProcessRepositoryAst repo, Dictionary<string, ProcessDefinitionAst> processIndex)
    {
        foreach (var process in repo.Processes)
        {
            var resolved = new List<string>();
            foreach (var raw in process.CalledSubProcessIds)
            {
                var key = Path.GetFileNameWithoutExtension(raw);
                if (processIndex.TryGetValue(key, out var sub)) resolved.Add(sub.Id);
            }
            process.CalledSubProcessIds.Clear();
            process.CalledSubProcessIds.AddRange(resolved.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void RelateResources(ProcessRepositoryAst repo)
    {
        foreach (var process in repo.Processes)
        foreach (var resource in repo.Resources)
        {
            var text = File.ReadAllText(process.Source!.FilePath);
            if (text.Contains(resource.Name, StringComparison.OrdinalIgnoreCase) || (process.InitialMessageName != null && resource.RelatedMessageNames.Any(m => string.Equals(m, process.InitialMessageName, StringComparison.OrdinalIgnoreCase))))
            {
                resource.UsedByProcessIds.Add(process.Id);
                process.ResourceIds.Add(resource.Id);
            }
        }
    }

    private static void GenerateIntegrationFacades(ProcessRepositoryAst repo)
    {
        foreach (var resource in repo.Resources.ToList())
        {
            var endpoint = new IntegrationEndpointDefinitionAst
            {
                Name = resource.Name + "_Facade",
                Kind = ProcessNodeKind.Resource,
                SourceResourceId = resource.Id,
                Source = resource.Source
            };

            if (!string.IsNullOrWhiteSpace(resource.Address) && (resource.Address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || resource.Address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                endpoint.FacadeKind = IntegrationFacadeKind.ApimApi;
                endpoint.BackendUrl = resource.Address;
                endpoint.DisplayPath = "/" + resource.Name.ToLowerInvariant();
                endpoint.HttpMethod = "POST";
                endpoint.ContractName = resource.Name + "Api";
                endpoint.Policies["authentication"] = "managed-identity-or-subscription-key";
                endpoint.Policies["rewrite-uri"] = endpoint.DisplayPath!;
            }
            else if (!string.IsNullOrWhiteSpace(resource.Address) && resource.Address.StartsWith("sb://", StringComparison.OrdinalIgnoreCase))
            {
                endpoint.FacadeKind = IntegrationFacadeKind.ServiceBusQueue;
                endpoint.QueueOrTopicName = resource.Name.ToLowerInvariant();
                endpoint.ContractName = resource.Name + "Queue";
                endpoint.Policies["transport"] = "service-bus";
            }
            else
            {
                endpoint.FacadeKind = IntegrationFacadeKind.DirectResource;
                endpoint.ContractName = resource.Name + "Direct";
            }

            repo.Children.Add(endpoint);
        }
    }

    private static bool IsProcessFile(string f) => Path.GetExtension(f).Equals(".process", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f).Equals(".subprocess", StringComparison.OrdinalIgnoreCase);
    private static SourceLocation Line(string path, XElement node) { var li = (System.Xml.IXmlLineInfo)node; return new SourceLocation { FilePath = path, Line = li.HasLineInfo() ? li.LineNumber : null, Column = li.HasLineInfo() ? li.LinePosition : null }; }
    private static string DetectVersion(XDocument doc) { var root = doc.Root?.Name.NamespaceName ?? string.Empty; return root.Contains("bw6", StringComparison.OrdinalIgnoreCase) || root.Contains("businessstudio", StringComparison.OrdinalIgnoreCase) ? "BW6" : "BW5"; }
    private static string? ExtractAddress(string text) { var http = Regex.Match(text, @"https?://[^\""""'\s<>]+", RegexOptions.IgnoreCase).Value; if (!string.IsNullOrWhiteSpace(http)) return http; var sb = Regex.Match(text, @"sb://[^\""""'\s<>]+", RegexOptions.IgnoreCase).Value; if (!string.IsNullOrWhiteSpace(sb)) return sb; var jdbc = Regex.Match(text, @"jdbc:[^\""""'\s<>]+", RegexOptions.IgnoreCase).Value; if (!string.IsNullOrWhiteSpace(jdbc)) return jdbc; return null; }
    private static ActivitySemanticKind InferActivity(string type, string localName) { var t=(type+" "+localName).ToLowerInvariant(); if (t.Contains("start")||t.Contains("receivehttp")) return ActivitySemanticKind.Start; if (t.Contains("end")) return ActivitySemanticKind.End; if (t.Contains("mapper")||t.Contains("mapdata")) return ActivitySemanticKind.Mapper; if (t.Contains("assign")) return ActivitySemanticKind.Assignment; if (t.Contains("choice")||t.Contains("condition")) return ActivitySemanticKind.Decision; if (t.Contains("iterate")||t.Contains("foreach")||t.Contains("repeat")) return ActivitySemanticKind.Loop; if (t.Contains("callprocess")) return ActivitySemanticKind.SubProcessCall; if (t.Contains("service")||t.Contains("http")||t.Contains("jdbc")||t.Contains("soap")) return ActivitySemanticKind.ServiceCall; if (t.Contains("receive")) return ActivitySemanticKind.Receive; if (t.Contains("send")||t.Contains("publish")) return ActivitySemanticKind.Send; return ActivitySemanticKind.Unknown; }
    private static TransitionSemanticKind InferTransition(string? type) { var t=type?.ToLowerInvariant()??""; if (t.Contains("success")) return TransitionSemanticKind.Success; if (t.Contains("condition")) return TransitionSemanticKind.Conditional; if (t.Contains("exception")) return TransitionSemanticKind.Exception; if (t.Contains("timeout")) return TransitionSemanticKind.Timeout; if (t.Contains("otherwise")) return TransitionSemanticKind.Otherwise; return TransitionSemanticKind.Unknown; }
}
