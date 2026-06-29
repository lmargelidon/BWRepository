using Microsoft.AspNetCore.Mvc;

namespace ProcessAst.RestFacade.Controllers;

[ApiController]
[Route("api/facades")]
public class BackendFacadeController : ControllerBase
{
    [HttpPost("{serviceName}")]
    public ActionResult<FacadeResponse> Post([FromRoute] string serviceName, [FromBody] FacadeRequest request)
    {
        return Ok(new FacadeResponse
        {
            Status = "accepted",
            Endpoint = $"api/facades/{serviceName}",
            Echo = new { serviceName, request.MessageName, request.Payload, request.Metadata }
        });
    }

    [HttpGet("{serviceName}/health")]
    public IActionResult Health([FromRoute] string serviceName)
    {
        return Ok(new { serviceName, status = "healthy", transport = "http" });
    }
}
