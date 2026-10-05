using Banking.Application.Common.Interfaces;

namespace Banking.Application.Tests;

/// <summary>Gateway bank ngoài giả cho test: chỉ định trước kết quả của Send và Query.</summary>
public sealed class FakeExternalBankGateway(ExternalResult sendResult, ExternalResult? queryResult = null)
    : IExternalBankGateway
{
    public Task<ExternalResult> SendAsync(Guid transferId, string? externalBankName, decimal amount, CancellationToken ct)
        => Task.FromResult(sendResult);

    public Task<ExternalResult> QueryStatusAsync(Guid transferId, decimal amount, CancellationToken ct)
        => Task.FromResult(queryResult ?? sendResult);
}
