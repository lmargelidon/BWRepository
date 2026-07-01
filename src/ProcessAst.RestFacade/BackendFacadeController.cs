using Microsoft.AspNetCore.Mvc;
namespace ProcessAst.RestFacade.Controllers;
[ApiController]
[Route("api/backend-facade")]
public sealed class BackendFacadeController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] object payload) => Ok(new { ok = true, payload });
}
