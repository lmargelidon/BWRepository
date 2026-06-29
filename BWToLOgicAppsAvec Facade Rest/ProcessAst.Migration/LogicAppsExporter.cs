using System.Text.Json;
using ProcessAst.Core;

namespace ProcessAst.Migration;

public sealed class LogicAppsExporter
{
    public string ExportWorkflow(ProcessDefinitionAst process, MigrationRuleSet? rules = null)
    {
        var ruleList = rules?.Rules ?? DefaultRules();
        var triggerType = process.Activities.Any(a => a.ActivityType.Equals("ReceiveHttpActivity", StringComparison.OrdinalIgnoreCase)) ? "Request" : "Recurrence";
        var actions = new Dictionary<string, object?>();

        foreach (var variable in process.LocalVariables)
        {
            actions[$"Init_{Safe(variable.Name)}"] = new { type = "InitializeVariable", inputs = new { variables = new[] { new { name = variable.Name, type = MapVariableType(variable.DataType), value = variable.DefaultValue ?? string.Empty } } }, runAfter = new Dictionary<string, string[]>() };
        }

        foreach (var activity in process.Activities)
        {
            var hit = ruleList.FirstOrDefault(r => r.SourceActivityType.Equals(activity.ActivityType, StringComparison.OrdinalIgnoreCase));
            actions[Safe(activity.Name)] = hit?.TargetConstruct switch
            {
                "Request trigger" => new { type = "Compose", inputs = "Inbound request handled by trigger" },
                "HTTP action" => new { type = "Http", inputs = new { method = "POST", uri = activity.Inputs.TryGetValue("url", out var u) ? u : "https://example.org" }, runAfter = new Dictionary<string, string[]>() },
                "Initialize/Set variable or Compose" => new { type = "Compose", inputs = activity.Name, runAfter = new Dictionary<string, string[]>() },
                "Compose or Transform XML" => new { type = "Compose", inputs = process.Mappings.FirstOrDefault(m => string.Equals(m.OwnerActivityId, activity.Name, StringComparison.OrdinalIgnoreCase))?.Entries.Select(e => new { e.TargetPath, e.NormalizedExpression }), runAfter = new Dictionary<string, string[]>() },
                "REST facade endpoint" => new { type = "Http", inputs = new { method = "POST", uri = "https://facade-host/api/facades/default", body = new { process = process.Name, activity = activity.Name } }, runAfter = new Dictionary<string, string[]>() },
                "Nested Logic App" => new { type = "Workflow", inputs = new { host = new { workflow = new { id = "child-workflow" } } }, runAfter = new Dictionary<string, string[]>() },
                _ => new { type = "Compose", inputs = $"Manual mapping required for {activity.ActivityType}", runAfter = new Dictionary<string, string[]>() }
            };
        }

        var model = new
        {
            definition = new
            {
                schema = "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2019-05-01/workflowdefinition.json#",
                contentVersion = "1.0.0.0",
                parameters = new Dictionary<string, object?>(),
                triggers = triggerType == "Request"
                    ? new Dictionary<string, object?> { ["manual"] = new { type = "Request", kind = "Http", inputs = new { schema = new { type = "object", properties = new { } } } } }
                    : new Dictionary<string, object?> { ["recurrence"] = new { type = "Recurrence", recurrence = new { frequency = "Minute", interval = 5 } } },
                actions,
                outputs = new Dictionary<string, object?>()
            },
            kind = "Stateful"
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ExportWorkflow(ProcessDefinitionAst process, string messageName, IEnumerable<VariableDefinitionAst>? globalVariables, IEnumerable<ResourceDefinitionAst>? resources, IEnumerable<IntegrationEndpointDefinitionAst>? integrationEndpoints, MigrationRuleSet? rules = null)
    {
        var ruleList = rules?.Rules ?? DefaultRules();
        var actions = new Dictionary<string, object?>();

        actions["Init_MessageName"] = new { type = "InitializeVariable", inputs = new { variables = new[] { new { name = "messageName", type = "string", value = messageName } } }, runAfter = new Dictionary<string, string[]>() };

        foreach (var variable in process.LocalVariables)
            actions[$"Init_{Safe(variable.Name)}"] = new { type = "InitializeVariable", inputs = new { variables = new[] { new { name = variable.Name, type = MapVariableType(variable.DataType), value = variable.DefaultValue ?? string.Empty } } }, runAfter = new Dictionary<string, string[]>() };

        foreach (var gv in globalVariables ?? Enumerable.Empty<VariableDefinitionAst>())
            actions[$"Global_{Safe(gv.Name)}"] = new { type = "InitializeVariable", inputs = new { variables = new[] { new { name = $"global__{gv.Name}", type = MapVariableType(gv.DataType), value = gv.DefaultValue ?? string.Empty } } }, runAfter = new Dictionary<string, string[]>() };

        foreach (var activity in process.Activities)
        {
            var hit = ruleList.FirstOrDefault(r => r.SourceActivityType.Equals(activity.ActivityType, StringComparison.OrdinalIgnoreCase));
            actions[Safe(activity.Name)] = hit?.TargetConstruct switch
            {
                "Request trigger" => new { type = "Compose", inputs = "Inbound request handled by trigger", runAfter = new Dictionary<string, string[]>() },
                "HTTP action" => new { type = "Http", inputs = new { method = "POST", uri = SelectFacadeUrl(integrationEndpoints), body = new { messageName, process = process.Name, activity = activity.Name } }, runAfter = new Dictionary<string, string[]>() },
                "Initialize/Set variable or Compose" => new { type = "Compose", inputs = activity.Name, runAfter = new Dictionary<string, string[]>() },
                "Compose or Transform XML" => new { type = "Compose", inputs = process.Mappings.SelectMany(m => m.Entries).Select(e => new { e.TargetPath, e.NormalizedExpression }), runAfter = new Dictionary<string, string[]>() },
                "REST facade endpoint" => new { type = "Http", inputs = new { method = "POST", uri = SelectFacadeUrl(integrationEndpoints), body = new { messageName, dispatch = "Dispatch message", resources = resources?.Select(r => new { r.Name, r.ResourceType, r.Address }) } }, runAfter = new Dictionary<string, string[]>() },
                "Nested Logic App" => new { type = "Workflow", inputs = new { host = new { workflow = new { id = "child-workflow" } } }, runAfter = new Dictionary<string, string[]>() },
                _ => new { type = "Compose", inputs = $"Manual mapping required for {activity.ActivityType}", runAfter = new Dictionary<string, string[]>() }
            };
        }

        var model = new
        {
            definition = new
            {
                schema = "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2019-05-01/workflowdefinition.json#",
                contentVersion = "1.0.0.0",
                parameters = new Dictionary<string, object?>(),
                triggers = new Dictionary<string, object?> { ["manual"] = new { type = "Request", kind = "Http", inputs = new { schema = new { type = "object", required = new[] { "message" }, properties = new { message = new { type = "string", @default = messageName } } } } } },
                actions,
                outputs = new Dictionary<string, object?>()
            },
            kind = "Stateful"
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<MigrationRule> DefaultRules() =>
    [
        new() { SourceActivityType = "MapperActivity", CanonicalCapability = "DataTransformation", TargetConstruct = "Compose or Transform XML", Hint = "Use Compose or XML transform." },
        new() { SourceActivityType = "AssignActivity", CanonicalCapability = "VariableAssignment", TargetConstruct = "Initialize/Set variable or Compose", Hint = "Prefer compose for immutable flow." },
        new() { SourceActivityType = "HttpActivity", CanonicalCapability = "HttpInvocation", TargetConstruct = "HTTP action", Hint = "Route through generated REST facade controller." },
        new() { SourceActivityType = "CallProcessActivity", CanonicalCapability = "WorkflowInvocation", TargetConstruct = "Nested Logic App", Hint = "Convert subprocess to child workflow." },
        new() { SourceActivityType = "ReceiveHttpActivity", CanonicalCapability = "MessageTrigger", TargetConstruct = "Request trigger", Hint = "Request trigger with JSON schema." },
        new() { SourceActivityType = "SendActivity", CanonicalCapability = "MessageDispatch", TargetConstruct = "REST facade endpoint", Hint = "Route outbound message through REST facade controller." }
    ];

    private static string MapVariableType(string dataType) => dataType.ToLowerInvariant() switch { "int" or "integer" => "integer", "bool" or "boolean" => "boolean", _ => "string" };
    private static string Safe(string name) => string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    private static string SelectFacadeUrl(IEnumerable<IntegrationEndpointDefinitionAst>? endpoints)
    {
        var route = endpoints?.FirstOrDefault()?.ContractName?.ToLowerInvariant() ?? "default";
        return $"https://facade-host/api/facades/{route}";
    }
}
