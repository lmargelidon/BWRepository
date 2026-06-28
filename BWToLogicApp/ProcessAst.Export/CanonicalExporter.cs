using System.Text.Json;
using ProcessAst.Core;

namespace ProcessAst.Export;

public sealed class CanonicalExporter
{
    public string Export(ProcessRepositoryAst repository)
    {
        var model = new
        {
            modelType = "CanonicalMigrationModelV5",
            processes = repository.Processes.Select(p => new
            {
                p.Id,
                p.Name,
                p.IsSubProcess,
                p.InitialMessageName,
                bwVersion = p.Metadata.TryGetValue("bwVersion", out var v) ? v : null,
                activities = p.Activities.Select(a => new { a.Id, a.Name, a.ActivityType, a.SemanticKind }),
                transitions = p.Transitions.Select(t => new { t.Id, t.FromActivityId, t.ToActivityId, t.SemanticKind, t.ConditionExpression }),
                mappings = p.Mappings.Select(m => new { m.Id, m.Name, entries = m.Entries.Select(e => new { e.TargetPath, e.SourceExpression, e.NormalizedExpression, e.ExpressionKind, e.Transform }) }),
                localVariables = p.LocalVariables.Select(vv => new { vv.Id, vv.Name, vv.Scope, vv.DataType, vv.DefaultValue, vv.Expression }),
                resources = p.ResourceIds
            }),
            globalVariables = repository.GlobalVariables.Select(v => new { v.Id, v.Name, v.Scope, v.DataType, v.DefaultValue }),
            resources = repository.Resources.Select(r => new { r.Id, r.Name, r.ResourceType, r.Address, r.UsedByProcessIds, r.RelatedMessageNames })
        };
        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }
}
