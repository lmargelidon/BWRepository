namespace ProcessAst.RestFacade;

public sealed class FacadeRequest
{
    public string? MessageName { get; set; }
    public object? Payload { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
