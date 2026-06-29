namespace ProcessAst.Core;

public enum IntegrationFacadeKind { ApimApi, ServiceBusQueue, ServiceBusTopic, DirectResource, Unknown }

public sealed class IntegrationEndpointDefinitionAst : AstNode
{
    public IntegrationFacadeKind FacadeKind { get; set; } = IntegrationFacadeKind.Unknown;
    public string SourceResourceId { get; set; } = "";
    public string? DisplayPath { get; set; }
    public string? BackendUrl { get; set; }
    public string? HttpMethod { get; set; }
    public string? QueueOrTopicName { get; set; }
    public string? ContractName { get; set; }
    public Dictionary<string, string> Policies { get; } = new(StringComparer.OrdinalIgnoreCase);
}
