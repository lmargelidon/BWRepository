namespace ProcessAst.Migration;

public enum InvocationStyle
{
    Unknown,
    SynchronousRequestResponse,
    AsynchronousCommand,
    EventDriven
}

public sealed class BusinessExecutionIntent
{
    public string MessageId { get; init; } = "";
    public string ProcessReference { get; init; } = "";
    public InvocationStyle InvocationStyle { get; init; } = InvocationStyle.Unknown;
    public bool ResponseExpected { get; init; }
    public bool ExtendedRequestFormat { get; init; }
    public List<BackendInvocationPlan> BackendPlans { get; } = new();
}

public sealed class BackendInvocationPlan
{
    public int Level { get; init; }
    public int PriorityInLevel { get; init; }
    public string SheetId { get; init; } = "";
    public bool IsMandatory { get; init; }
    public string BackendName { get; init; } = "";
    public string RequestId { get; init; } = "";
    public bool WaitForResponse { get; init; }
    public bool IgnoreResponse { get; init; }
    public bool ConvertRequest { get; init; }
    public bool ConvertResponse { get; init; }
}
