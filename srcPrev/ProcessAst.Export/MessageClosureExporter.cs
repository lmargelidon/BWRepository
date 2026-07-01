using System.Text.Json;
using ProcessAst.Migration;

namespace ProcessAst.Export;

public sealed class MessageClosureExporter
{
    public string Export(MessageMigrationClosure closure)
    {
        var model = new
        {
            modelType = "MessageMigrationClosureV9",
            closure.MessageName,
            closure.ResolvedInternalServiceName,
            businessConfiguration = closure.BusinessConfiguration is null
                ? null
                : new
                {
                    closure.BusinessConfiguration.MessageId,
                    closure.BusinessConfiguration.Version,
                    closure.BusinessConfiguration.InternalServiceName,
                    closure.BusinessConfiguration.IsActive,
                    closure.BusinessConfiguration.ExtendedRequestFormat,
                    closure.BusinessConfiguration.ReturnResponse,
                    backendSheets = closure.BusinessConfiguration.BackendSheets.Select(s => new
                    {
                        s.Level,
                        s.PriorityInLevel,
                        s.SheetId,
                        s.IsMandatory,
                        backendRequest = s.BackendRequest is null
                            ? null
                            : new
                            {
                                s.BackendRequest.BackendName,
                                s.BackendRequest.RequestId,
                                s.BackendRequest.Version,
                                s.BackendRequest.IncludeSheetId,
                                s.BackendRequest.RequestState,
                                s.BackendRequest.WaitForResponse,
                                s.BackendRequest.IgnoreResponse,
                                s.BackendRequest.ConvertBackendRequest,
                                s.BackendRequest.ConvertBackendResponse
                            }
                    })
                },
            executionIntent = closure.ExecutionIntent is null
                ? null
                : new
                {
                    closure.ExecutionIntent.MessageId,
                    closure.ExecutionIntent.ProcessReference,
                    invocationStyle = closure.ExecutionIntent.InvocationStyle.ToString(),
                    closure.ExecutionIntent.ResponseExpected,
                    closure.ExecutionIntent.ExtendedRequestFormat,
                    backendPlans = closure.ExecutionIntent.BackendPlans.Select(p => new
                    {
                        p.Level,
                        p.PriorityInLevel,
                        p.SheetId,
                        p.IsMandatory,
                        p.BackendName,
                        p.RequestId,
                        p.WaitForResponse,
                        p.IgnoreResponse,
                        p.ConvertRequest,
                        p.ConvertResponse
                    })
                },
            messages = closure.Messages.Select(m => new { m.Id, m.Name, m.SchemaType, m.EntryProcessIds, m.RelatedResourceIds }),
            processes = closure.Processes.Select(p => new
            {
                p.Id,
                p.Name,
                p.IsSubProcess,
                p.InitialMessageName,
                bwVersion = p.Metadata.TryGetValue("bwVersion", out var v) ? v : null,
                activities = p.Activities.Select(a => new { a.Id, a.Name, a.ActivityType, a.SemanticKind }),
                transitions = p.Transitions.Select(t => new { t.Id, t.FromActivityId, t.ToActivityId, t.SemanticKind, t.ConditionExpression }),
                calledSubProcesses = p.CalledSubProcessIds,
                resources = p.ResourceIds,
                mappings = p.Mappings.Select(m => new { m.Id, m.Name, entries = m.Entries.Select(e => new { e.TargetPath, e.SourceExpression, e.NormalizedExpression, e.ExpressionKind, e.Transform }) }),
                localVariables = p.LocalVariables.Select(vv => new { vv.Id, vv.Name, vv.Scope, vv.DataType, vv.DefaultValue, vv.Expression })
            }),
            globalVariables = closure.GlobalVariables.Select(v => new { v.Id, v.Name, v.Scope, v.DataType, v.DefaultValue, v.Expression }),
            resources = closure.Resources.Select(r => new { r.Id, r.Name, r.ResourceType, r.Address, r.UsedByProcessIds, r.RelatedMessageNames }),
            integrationEndpoints = closure.IntegrationEndpoints.Select(e => new { e.Id, e.Name, e.FacadeKind, e.SourceResourceId, e.DisplayPath, e.BackendUrl, e.HttpMethod, e.QueueOrTopicName, e.ContractName, e.Policies })
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }
}
