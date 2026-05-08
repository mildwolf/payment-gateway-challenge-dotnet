using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Tests.Unit;

public class FakeBankService : IBankService
{
    public bool? Result { get; set; }
    public string? AuthorizationCode { get; set; }
    public int ProcessCallCount { get; private set; }
    public TaskCompletionSource<BankResponse?>? CompletionSource { get; set; }

    public async Task<BankResponse?> ProcessPaymentAsync(PostPaymentRequest request)
    {
        ProcessCallCount++;

        if (CompletionSource is not null)
            return await CompletionSource.Task;

        if (Result is null)
            return null;

        return new BankResponse
        {
            Authorized = Result,
            AuthorizationCode = AuthorizationCode ?? (Result is true ? "fake-auth-code" : string.Empty)
        };
    }
}
