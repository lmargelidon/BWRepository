using Microsoft.AspNetCore.Mvc;
namespace ProcessAst.RestFacade.Controllers;
[ApiController]
[Route("api/generated-facades")]
public class GeneratedFacadeController : ControllerBase
{
    [HttpPost("default")]
    public IActionResult Default([FromBody] object payload) => Ok(new { facade = "default", backend = "n/a", payload });
}
