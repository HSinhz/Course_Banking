using Banking.Application.Common.Exceptions;
using Banking.Application.Transfers.Commands.Interbank;
using Banking.Application.Transfers.Commands.TransferMoney;
using Banking.Application.Transfers.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Banking.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/transfers")]
public class TransfersController(ISender mediator) : ControllerBase
{
    public record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount);
    public record InterbankRequest(Guid FromAccountId, string ExternalBankName, decimal Amount);

    // Chuyển NỘI BỘ (Giai đoạn 1) — atomic, xong ngay.
    [HttpPost]
    public async Task<IActionResult> Transfer(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] TransferRequest body,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new TransferMoneyCommand(idempotencyKey ?? "", body.FromAccountId, body.ToAccountId, body.Amount), ct);
            return Ok(result);
        }
        catch (MissingIdempotencyKeyException ex) { return BadRequest(new { error = ex.Message }); }
        catch (ConcurrentRequestException ex) { return Conflict(new { error = ex.Message }); }
        catch (InsufficientFundsException ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AccountNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // Chuyển LIÊN NGÂN HÀNG (Giai đoạn 2) — trả "Pending", saga hoàn tất bất đồng bộ.
    [HttpPost("interbank")]
    public async Task<IActionResult> Interbank(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] InterbankRequest body,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new InterbankTransferCommand(idempotencyKey ?? "", body.FromAccountId, body.ExternalBankName, body.Amount), ct);
            return Ok(result);
        }
        catch (MissingIdempotencyKeyException ex) { return BadRequest(new { error = ex.Message }); }
        catch (ConcurrentRequestException ex) { return Conflict(new { error = ex.Message }); }
        catch (InsufficientFundsException ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AccountNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    // Tra cứu trạng thái giao dịch (status inquiry).
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        var dto = await mediator.Send(new GetTransferStatusQuery(id), ct);
        return dto is null ? NotFound() : Ok(dto);
    }
}
