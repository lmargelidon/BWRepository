using ProcessAst.Core;

namespace ProcessAst.Migration;

public sealed class MessageMigrationClosure
{
    public string MessageName { get; set; } = "";
    public string? ResolvedInternalServiceName { get; set; }
    public BusinessMessageConfiguration? BusinessConfiguration { get; set; }
    public BusinessExecutionIntent? ExecutionIntent { get; set; }
    public List<ProcessDefinitionAst> Processes { get; } = new();
    public List<VariableDefinitionAst> GlobalVariables { get; } = new();
    public List<ResourceDefinitionAst> Resources { get; } = new();
    public List<MessageDefinitionAst> Messages { get; } = new();
    public List<IntegrationEndpointDefinitionAst> IntegrationEndpoints { get; } = new();
}

public sealed class MessageClosureBuilder
{
    public MessageMigrationClosure Build(ProcessRepositoryAst repo, string messageName)
        => Build(repo, messageName, configurationXmlPath: null);

    public MessageMigrationClosure Build(ProcessRepositoryAst repo, string messageName, string? configurationXmlPath)
    {
        if (repo is null)
            throw new ArgumentNullException(nameof(repo));

        if (string.IsNullOrWhiteSpace(messageName))
            throw new ArgumentException("messageName is required.", nameof(messageName));

        var closure = new MessageMigrationClosure { MessageName = messageName };
        var processById = repo.Processes.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var selected = new Dictionary<string, ProcessDefinitionAst>(StringComparer.OrdinalIgnoreCase);

        BusinessMessageConfiguration? businessConfig = null;
        if (!string.IsNullOrWhiteSpace(configurationXmlPath))
        {
            businessConfig = new BusinessMessageConfigurationResolver().Resolve(configurationXmlPath, messageName);
            closure.BusinessConfiguration = businessConfig;
            closure.ResolvedInternalServiceName = businessConfig?.InternalServiceName;

            if (businessConfig is not null)
                closure.ExecutionIntent = new BusinessExecutionIntentFactory().Create(businessConfig);
        }

        var starters = repo.Processes
            .Where(p => string.Equals(p.InitialMessageName, messageName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (starters.Count == 0 && businessConfig is not null && businessConfig.IsActive && !string.IsNullOrWhiteSpace(businessConfig.InternalServiceName))
        {
            var normalized = NormalizeProcessReference(businessConfig.InternalServiceName);

            starters = repo.Processes
                .Where(p =>
                    string.Equals(p.Id, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeProcessReference(p.Name), normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeProcessReference(p.Id), normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var stack = new Stack<ProcessDefinitionAst>(starters);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!selected.TryAdd(current.Id, current))
                continue;

            foreach (var childId in current.CalledSubProcessIds)
            {
                if (processById.TryGetValue(childId, out var child))
                    stack.Push(child);
            }
        }

        foreach (var process in selected.Values)
            closure.Processes.Add(process);

        var globalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in closure.Processes)
        {
            foreach (var mapping in process.Mappings)
            {
                foreach (var entry in mapping.Entries)
                {
                    var expr = entry.NormalizedExpression ?? entry.SourceExpression ?? "";
                    if (expr.StartsWith("global::", StringComparison.OrdinalIgnoreCase))
                        globalNames.Add(expr.Substring("global::".Length));
                }
            }
        }

        foreach (var gv in repo.GlobalVariables.Where(v => globalNames.Contains(v.Name)))
            closure.GlobalVariables.Add(gv);

        var resourceIds = new HashSet<string>(
            closure.Processes.SelectMany(p => p.ResourceIds),
            StringComparer.OrdinalIgnoreCase);

        foreach (var resource in repo.Resources.Where(r => resourceIds.Contains(r.Id)))
            closure.Resources.Add(resource);

        foreach (var msg in repo.RootMessages.Where(m => string.Equals(m.Name, messageName, StringComparison.OrdinalIgnoreCase)))
            closure.Messages.Add(msg);

        foreach (var endpoint in repo.Children
                     .OfType<IntegrationEndpointDefinitionAst>()
                     .Where(e => resourceIds.Contains(e.SourceResourceId)))
        {
            closure.IntegrationEndpoints.Add(endpoint);
        }

        return closure;
    }

    private static string NormalizeProcessReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Replace('\\', '/').Trim();

        if (normalized.EndsWith(".process", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^".process".Length];

        if (normalized.StartsWith("/"))
            normalized = normalized[1..];

        var last = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return last ?? normalized;
    }
}
