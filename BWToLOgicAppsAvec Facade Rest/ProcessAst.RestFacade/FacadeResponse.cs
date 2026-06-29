namespace ProcessAst.RestFacade;

public sealed class FacadeResponse
{
    public string Status { get; set; } = "accepted";
    public string Endpoint { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public object? Echo { get; set; }
}
