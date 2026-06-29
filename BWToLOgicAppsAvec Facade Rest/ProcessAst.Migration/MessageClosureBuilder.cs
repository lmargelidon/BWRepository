using ProcessAst.Core;

namespace ProcessAst.Migration;

public sealed class MessageMigrationClosure
{
    public string MessageName { get; set; } = "";
    public List<ProcessDefinitionAst> Processes { get; } = new();
    public List<VariableDefinitionAst> GlobalVariables { get; } = new();
    public List<ResourceDefinitionAst> Resources { get; } = new();
    public List<MessageDefinitionAst> Messages { get; } = new();
    public List<IntegrationEndpointDefinitionAst> IntegrationEndpoints { get; } = new();
}

public sealed class MessageClosureBuilder
{
    public MessageMigrationClosure Build(ProcessRepositoryAst repo, string messageName)
    {
        var closure = new MessageMigrationClosure { MessageName = messageName };
        var processById = repo.Processes.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, ProcessDefinitionAst>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<ProcessDefinitionAst>(repo.Processes.Where(p => string.Equals(p.InitialMessageName, messageName, StringComparison.OrdinalIgnoreCase)));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!selected.TryAdd(current.Id, current)) continue;
            foreach (var childId in current.CalledSubProcessIds)
                if (processById.TryGetValue(childId, out var child)) stack.Push(child);
        }

        foreach (var process in selected.Values) closure.Processes.Add(process);

        var globalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in closure.Processes)
        foreach (var mapping in process.Mappings)
        foreach (var entry in mapping.Entries)
        {
            var expr = entry.NormalizedExpression ?? entry.SourceExpression;
            if (expr.StartsWith("global::", StringComparison.OrdinalIgnoreCase)) globalNames.Add(expr.Substring("global::".Length));
        }

        foreach (var gv in repo.GlobalVariables.Where(v => globalNames.Contains(v.Name))) closure.GlobalVariables.Add(gv);

        var resourceIds = new HashSet<string>(closure.Processes.SelectMany(p => p.ResourceIds), StringComparer.OrdinalIgnoreCase);
        foreach (var resource in repo.Resources.Where(r => resourceIds.Contains(r.Id))) closure.Resources.Add(resource);
        foreach (var msg in repo.RootMessages.Where(m => string.Equals(m.Name, messageName, StringComparison.OrdinalIgnoreCase))) closure.Messages.Add(msg);
        foreach (var endpoint in repo.Children.OfType<IntegrationEndpointDefinitionAst>().Where(e => resourceIds.Contains(e.SourceResourceId))) closure.IntegrationEndpoints.Add(endpoint);

        return closure;
    }
}
