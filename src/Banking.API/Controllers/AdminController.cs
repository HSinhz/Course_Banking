using Banking.Application.Transfers.Commands.Reconcile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/admin")]
public class AdminController(ISender mediator) : ControllerBase
{
    /// <summary>Chạy đối soát: giải quyết các lệnh liên NH còn treo (Pending) bằng cách tra cứu lại bank ngoài.</summary>
    [HttpPost("reconcile")]
    public async Task<IActionResult> Reconcile(CancellationToken ct)
    {
        var resolved = await mediator.Send(new ReconcileCommand(), ct);
        return Ok(new { resolved });
    }
}
