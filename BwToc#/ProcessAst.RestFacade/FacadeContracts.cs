namespace ProcessAst.RestFacade;
public sealed class FacadeRequest { public string MessageName { get; set; } = string.Empty; public object? Payload { get; set; } }
public sealed class FacadeResponse { public bool Success { get; set; } public string? Backend { get; set; } public object? Result { get; set; } }
