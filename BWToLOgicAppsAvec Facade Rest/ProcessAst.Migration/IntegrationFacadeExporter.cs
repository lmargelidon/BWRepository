using System.Text.Json;
using ProcessAst.Core;

namespace ProcessAst.Migration;

public sealed class IntegrationFacadeExporter
{
    public string ExportApimArtifacts(IEnumerable<IntegrationEndpointDefinitionAst> endpoints)
    {
        var apis = endpoints.Where(e => e.FacadeKind == IntegrationFacadeKind.ApimApi).Select(e => new
        {
            name = e.ContractName,
            path = e.DisplayPath,
            backendUrl = e.BackendUrl,
            method = e.HttpMethod ?? "POST",
            policies = e.Policies
        });
        return JsonSerializer.Serialize(new { apis }, new JsonSerializerOptions { WriteIndented = true });
    }

    public string ExportServiceBusArtifacts(IEnumerable<IntegrationEndpointDefinitionAst> endpoints)
    {
        var entities = endpoints.Where(e => e.FacadeKind == IntegrationFacadeKind.ServiceBusQueue || e.FacadeKind == IntegrationFacadeKind.ServiceBusTopic).Select(e => new
        {
            name = e.QueueOrTopicName,
            kind = e.FacadeKind.ToString(),
            contract = e.ContractName,
            policies = e.Policies
        });
        return JsonSerializer.Serialize(new { entities }, new JsonSerializerOptions { WriteIndented = true });
    }
}
