using System.Text;
using ProcessAst.Core;

namespace ProcessAst.Migration;

public sealed class BwProcessCSharpExporter
{
    public string ExportProcess(ProcessDefinitionAst process, MigrationRuleSet? rules = null)
    {
        return ExportProcess(process, process.InitialMessageName ?? process.Name, null, null, null, rules);
    }

    public string ExportProcess(
        ProcessDefinitionAst process,
        string messageName,
        IEnumerable<VariableDefinitionAst>? globalVariables,
        IEnumerable<ResourceDefinitionAst>? resources,
        IEnumerable<IntegrationEndpointDefinitionAst>? integrationEndpoints,
        MigrationRuleSet? rules = null)
    {
        var className = SafeType(process.Name) + "BwProcess";
        var requestName = SafeType(process.Name) + "Context";
        var sb = new StringBuilder();

        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Nodes;");
        sb.AppendLine();
        sb.AppendLine("namespace Generated.ProcessAst.V10;");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly HttpClient _httpClient;");
        sb.AppendLine();
        sb.AppendLine($"    public {className}(HttpClient httpClient)");
        sb.AppendLine("    {");
        sb.AppendLine("        _httpClient = httpClient;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    public async Task ExecuteAsync({requestName} ctx, CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine($"        ctx.MessageName ??= \"{Escape(messageName)}\";");
        sb.AppendLine();

        foreach (var gv in globalVariables ?? Enumerable.Empty<VariableDefinitionAst>())
            sb.AppendLine($"        ctx.GlobalVariables.TryAdd(\"{Escape(gv.Name)}\", {FormatValue(gv.DefaultValue, gv.DataType)});");

        if ((globalVariables?.Any() ?? false) && process.LocalVariables.Count > 0)
            sb.AppendLine();

        foreach (var lv in process.LocalVariables)
            sb.AppendLine($"        ctx.LocalVariables.TryAdd(\"{Escape(lv.Name)}\", {FormatValue(lv.DefaultValue, lv.DataType)});");

        sb.AppendLine();
        sb.AppendLine("        var activityMap = new Dictionary<string, Func<Task>>(StringComparer.OrdinalIgnoreCase)");
        sb.AppendLine("        {");

        foreach (var activity in process.Activities)
            sb.AppendLine($"            [\"{Escape(activity.Id)}\"] = async () => await Execute_{SafeMember(activity.Name)}(ctx, cancellationToken),");

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        var transitions = new List<BwTransition>");
        sb.AppendLine("        {");

        foreach (var t in process.Transitions)
        {
            sb.AppendLine($"            new BwTransition(\"{Escape(t.FromActivityId)}\", \"{Escape(t.ToActivityId)}\", \"{Escape(t.SemanticKind.ToString())}\", \"{Escape(t.ConditionExpression ?? string.Empty)}\"),");
        }

        sb.AppendLine("        };");
        sb.AppendLine();
        sb.AppendLine("        ctx.Messages.Add(new BwMessageEnvelope");
        sb.AppendLine("        {");
        sb.AppendLine("            Name = ctx.MessageName ?? string.Empty,");
        sb.AppendLine("            Payload = ctx.Payload?.DeepClone() as JsonNode,");
        sb.AppendLine($"            SourceProcess = \"{Escape(process.Name)}\""
        + ");");
        sb.AppendLine("        });");
        sb.AppendLine();

        var startActivity = process.Activities.FirstOrDefault(a => a.SemanticKind is ActivitySemanticKind.Start or ActivitySemanticKind.Receive)
                            ?? process.Activities.FirstOrDefault();
        if (startActivity is not null)
        {
            sb.AppendLine($"        await ExecuteFromAsync(\"{Escape(startActivity.Id)}\", activityMap, transitions, ctx, cancellationToken);");
        }
        else
        {
            sb.AppendLine("        // No activities found in process.");
        }

        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var activity in process.Activities)
        {
            EmitActivityMethod(sb, process, activity, resources, integrationEndpoints);
        }

        sb.AppendLine("    private static async Task ExecuteFromAsync(string startId, Dictionary<string, Func<Task>> activityMap, List<BwTransition> transitions, " + requestName + " ctx, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine("        var current = startId;");
        sb.AppendLine("        var guard = 0;");
        sb.AppendLine();
        sb.AppendLine("        while (!string.IsNullOrWhiteSpace(current) && guard++ < 1000)");
        sb.AppendLine("        {");
        sb.AppendLine("            ctx.ExecutionTrace.Add(current);");
        sb.AppendLine("            if (activityMap.TryGetValue(current, out var action))");
        sb.AppendLine("                await action();");
        sb.AppendLine();
        sb.AppendLine("            var next = transitions.Where(t => string.Equals(t.FromActivityId, current, StringComparison.OrdinalIgnoreCase)).ToList();");
        sb.AppendLine("            if (next.Count == 0)");
        sb.AppendLine("                break;");
        sb.AppendLine();
        sb.AppendLine("            var selected = next.FirstOrDefault(t => EvaluateCondition(t, ctx)) ?? next.First(); ");
        sb.AppendLine("            current = selected.ToActivityId;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static bool EvaluateCondition(BwTransition transition, " + requestName + " ctx)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (string.IsNullOrWhiteSpace(transition.ConditionExpression))");
        sb.AppendLine("            return true;");
        sb.AppendLine();
        sb.AppendLine("        var expr = transition.ConditionExpression.Trim();");
        sb.AppendLine("        if (string.Equals(expr, \"otherwise\", StringComparison.OrdinalIgnoreCase))");
        sb.AppendLine("            return true;");
        sb.AppendLine("        if (string.Equals(expr, \"true\", StringComparison.OrdinalIgnoreCase))");
        sb.AppendLine("            return true;");
        sb.AppendLine("        if (string.Equals(expr, \"false\", StringComparison.OrdinalIgnoreCase))");
        sb.AppendLine("            return false;");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {requestName}");
        sb.AppendLine("{");
        sb.AppendLine("    public string? MessageName { get; set; }");
        sb.AppendLine("    public JsonNode? Payload { get; set; }");
        sb.AppendLine("    public Dictionary<string, object?> LocalVariables { get; } = new(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    public Dictionary<string, object?> GlobalVariables { get; } = new(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    public Dictionary<string, object?> ActivityOutputs { get; } = new(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    public List<BwMessageEnvelope> Messages { get; } = new();");
        sb.AppendLine("    public List<string> ExecutionTrace { get; } = new();");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public sealed record BwTransition(string FromActivityId, string ToActivityId, string SemanticKind, string ConditionExpression);");
        sb.AppendLine();
        sb.AppendLine("public sealed class BwMessageEnvelope");
        sb.AppendLine("{");
        sb.AppendLine("    public string Name { get; init; } = string.Empty;");
        sb.AppendLine("    public JsonNode? Payload { get; init; }");
        sb.AppendLine("    public string SourceProcess { get; init; } = string.Empty;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void EmitActivityMethod(StringBuilder sb, ProcessDefinitionAst process, ActivityDefinitionAst activity, IEnumerable<ResourceDefinitionAst>? resources, IEnumerable<IntegrationEndpointDefinitionAst>? endpoints)
    {
        var methodName = "Execute_" + SafeMember(activity.Name);
        sb.AppendLine($"    private async Task {methodName}({SafeType(process.Name)}Context ctx, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = new Dictionary<string, object?>();");

        switch (activity.SemanticKind)
        {
            case ActivitySemanticKind.Start:
            case ActivitySemanticKind.Receive:
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = new {{ accepted = true, message = ctx.MessageName }};");
                break;

            case ActivitySemanticKind.Mapper:
                sb.AppendLine("        ctx.ActivityOutputs[\"" + Escape(activity.Name) + "\"] = new object[]");
                sb.AppendLine("        {");
                foreach (var entry in process.Mappings.SelectMany(m => m.Entries))
                    sb.AppendLine($"            new {{ target = \"{Escape(entry.TargetPath)}\", source = \"{Escape(entry.SourceExpression)}\", normalized = \"{Escape(entry.NormalizedExpression ?? string.Empty)}\" }},");
                sb.AppendLine("        };");
                break;

            case ActivitySemanticKind.Assignment:
                sb.AppendLine($"        ctx.LocalVariables[\"{Escape(activity.Name)}\"] = \"assignment-executed\";");
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = ctx.LocalVariables[\"{Escape(activity.Name)}\"];");
                break;

            case ActivitySemanticKind.Decision:
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = \"decision-evaluated\";");
                break;

            case ActivitySemanticKind.Loop:
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = \"loop-placeholder\";");
                break;

            case ActivitySemanticKind.SubProcessCall:
                var childIds = process.CalledSubProcessIds.Any()
                    ? string.Join(", ", process.CalledSubProcessIds.Select(x => "\"" + Escape(x) + "\""))
                    : string.Empty;
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = new [] {{ {childIds} }};");
                break;

            case ActivitySemanticKind.ServiceCall:
                var serviceUrl = endpoints?.FirstOrDefault()?.BackendUrl
                                 ?? activity.Inputs.GetValueOrDefault("url")
                                 ?? resources?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Address))?.Address
                                 ?? "https://example.org";
                sb.AppendLine("        {");
                sb.AppendLine($"            using var response = await _httpClient.PostAsJsonAsync(\"{Escape(serviceUrl)}\", new");
                sb.AppendLine("            {");
                sb.AppendLine("                ctx.MessageName,");
                sb.AppendLine($"                process = \"{Escape(process.Name)}\",");
                sb.AppendLine($"                activity = \"{Escape(activity.Name)}\",");
                sb.AppendLine("                payload = ctx.Payload");
                sb.AppendLine("            }, cancellationToken);");
                sb.AppendLine($"            ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = (int)response.StatusCode;");
                sb.AppendLine("        }");
                break;

            case ActivitySemanticKind.Send:
                var route = endpoints?.FirstOrDefault()?.ContractName?.ToLowerInvariant() ?? "default";
                sb.AppendLine("        {");
                sb.AppendLine($"            using var response = await _httpClient.PostAsJsonAsync(\"/api/generated-facades/{Escape(route)}\", new");
                sb.AppendLine("            {");
                sb.AppendLine("                ctx.MessageName,");
                sb.AppendLine($"                fromProcess = \"{Escape(process.Name)}\",");
                sb.AppendLine($"                activity = \"{Escape(activity.Name)}\",");
                sb.AppendLine("                payload = ctx.Payload");
                sb.AppendLine("            }, cancellationToken);");
                sb.AppendLine($"            ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = (int)response.StatusCode;");
                sb.AppendLine("        }");
                break;

            case ActivitySemanticKind.End:
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = \"completed\";");
                break;

            default:
                sb.AppendLine($"        ctx.ActivityOutputs[\"{Escape(activity.Name)}\"] = \"manual-implementation-required:{Escape(activity.ActivityType)}\";");
                break;
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static string SafeType(string name)
    {
        var cleaned = string.Concat(name.Where(char.IsLetterOrDigit));
        return string.IsNullOrWhiteSpace(cleaned) ? "GeneratedProcess" : cleaned;
    }

    private static string SafeMember(string name)
    {
        var cleaned = string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        return string.IsNullOrWhiteSpace(cleaned) ? "Activity" : cleaned;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string FormatValue(string? value, string dataType)
    {
        if (string.IsNullOrWhiteSpace(value))
            return dataType.ToLowerInvariant() switch
            {
                "int" or "integer" => "0",
                "bool" or "boolean" => "false",
                _ => "string.Empty"
            };

        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => int.TryParse(value, out var i) ? i.ToString() : "0",
            "bool" or "boolean" => bool.TryParse(value, out var b) && b ? "true" : "false",
            _ => "\"" + Escape(value) + "\""
        };
    }
}
