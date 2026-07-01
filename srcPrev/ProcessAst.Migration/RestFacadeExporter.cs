using System.Text;
using System.Text.Json;
using ProcessAst.Core;
namespace ProcessAst.Migration;
public sealed class RestFacadeExporter
{
    public string ExportEndpoints(IEnumerable<IntegrationEndpointDefinitionAst> endpoints)
    {
        var model = new { endpoints = endpoints.Select(e => new { e.Name, route = "/api/facades/" + (e.ContractName ?? e.Name).ToLowerInvariant(), method = e.HttpMethod ?? "POST", e.BackendUrl, e.SourceResourceId, e.Policies }) };
        return JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
    }
    public string ExportControllerScaffold(IEnumerable<IntegrationEndpointDefinitionAst> endpoints)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine();
        sb.AppendLine("namespace ProcessAst.RestFacade.Controllers;");
        sb.AppendLine();
        sb.AppendLine("[ApiController]");
        sb.AppendLine("[Route("api/generated-facades")]");
        sb.AppendLine("public class GeneratedFacadeController : ControllerBase");
        sb.AppendLine("{");
        foreach (var e in endpoints)
        {
            var route = (e.ContractName ?? e.Name).ToLowerInvariant();
            var action = string.Concat((e.ContractName ?? e.Name).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            sb.AppendLine($"    [HttpPost("{route}")] public IActionResult {action}([FromBody] object payload) => Ok(new {{ facade = "{route}", backend = "{e.BackendUrl ?? "n/a"}", payload }});");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }
}
