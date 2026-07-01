using System.Text;
using System.Text.Json;
using ProcessAst.Core;

namespace ProcessAst.Migration;

public sealed class RestFacadeExporter
{
    public string ExportEndpoints(IEnumerable<IntegrationEndpointDefinitionAst> endpoints)
    {
        var model = new
        {
            endpoints = endpoints.Select(e => new
            {
                e.Name,
                route = "/api/facades/" + (e.ContractName ?? e.Name).ToLowerInvariant(),
                method = e.HttpMethod ?? "POST",
                backendUrl = e.BackendUrl,
                facadeKind = "RestHttpEndpoint",
                sourceResourceId = e.SourceResourceId,
                policies = e.Policies
            })
        };

        return JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public string ExportControllerScaffold(IEnumerable<IntegrationEndpointDefinitionAst> endpoints)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine();
        sb.AppendLine("namespace ProcessAst.RestFacade.Controllers;");
        sb.AppendLine();
        sb.AppendLine("[ApiController]");
        sb.AppendLine("[Route(\"api/generated-facades\")]");
        sb.AppendLine("public class GeneratedFacadeController : ControllerBase");
        sb.AppendLine("{");

        foreach (var e in endpoints)
        {
            var actionName = Safe(e.ContractName ?? e.Name);
            var route = (e.ContractName ?? e.Name).ToLowerInvariant();
            var backend = e.BackendUrl ?? "n/a";

            sb.AppendLine($"    [HttpPost(\"{route}\")]");
            sb.AppendLine($"    public IActionResult {actionName}([FromBody] object payload) => Ok(new {{ facade = \"{route}\", backend = \"{backend}\", payload }});");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Safe(string name)
    {
        return string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    }
}