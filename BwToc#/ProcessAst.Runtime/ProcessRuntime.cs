using System.Text.Json.Nodes;
namespace ProcessAst.Runtime;
public sealed class ProcessExecutionContext { public string MessageName { get; set; } = string.Empty; public JsonNode? Payload { get; set; } public Dictionary<string, object?> LocalVariables { get; } = new(StringComparer.OrdinalIgnoreCase); public Dictionary<string, object?> GlobalVariables { get; } = new(StringComparer.OrdinalIgnoreCase); public Dictionary<string, object?> ActivityOutputs { get; } = new(StringComparer.OrdinalIgnoreCase); public List<string> Trace { get; } = new(); }
public interface IGeneratedProcess { string ProcessName { get; } Task ExecuteAsync(ProcessExecutionContext context, CancellationToken cancellationToken = default); }
public interface IFacadeDispatcher { Task<object?> DispatchAsync(string route, object payload, CancellationToken cancellationToken = default); }
public sealed class InMemoryFacadeDispatcher : IFacadeDispatcher { public Task<object?> DispatchAsync(string route, object payload, CancellationToken cancellationToken = default) => Task.FromResult<object?>(new { route, payload, dispatched = true }); }
