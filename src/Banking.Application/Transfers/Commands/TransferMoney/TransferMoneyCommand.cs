using MediatR;

namespace Banking.Application.Transfers.Commands.TransferMoney;

public record TransferMoneyCommand(
    string IdempotencyKey,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount) : IRequest<TransferMoneyResult>;

public record TransferMoneyResult(
    Guid TransferId,
    decimal FromBalanceAfter,
    string Status,
    bool ServedFromCache = false);
