using System.Xml.Linq;
namespace ProcessAst.Migration;
public sealed class BusinessMessageConfiguration
{
    public string MessageId { get; init; } = "";
    public int Version { get; init; }
    public string InternalServiceName { get; init; } = "";
    public bool IsActive { get; init; }
    public bool ExtendedRequestFormat { get; init; }
    public bool ReturnResponse { get; init; }
    public List<BackendSheetConfiguration> BackendSheets { get; } = new();
}
public sealed class BackendSheetConfiguration
{
    public int Level { get; init; }
    public int PriorityInLevel { get; init; }
    public string SheetId { get; init; } = "";
    public bool IsMandatory { get; init; }
    public BackendRequestConfiguration? BackendRequest { get; init; }
}
public sealed class BackendRequestConfiguration
{
    public string BackendName { get; init; } = "";
    public string RequestId { get; init; } = "";
    public int Version { get; init; }
    public bool IncludeSheetId { get; init; }
    public string RequestState { get; init; } = "";
    public bool WaitForResponse { get; init; }
    public bool IgnoreResponse { get; init; }
    public bool ConvertBackendRequest { get; init; }
    public bool ConvertBackendResponse { get; init; }
}
public sealed class BusinessMessageConfigurationResolver
{
    public BusinessMessageConfiguration? Resolve(string xmlPath, string messageId) => null;
}
