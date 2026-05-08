using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Tests.Unit;

public class FakeBankService : IBankService
{
    public bool? Result { get; set; }
    public string? AuthorizationCode { get; set; }

    public Task<BankResponse?> ProcessPaymentAsync(PostPaymentRequest request)
    {
        if (Result is null)
            return Task.FromResult<BankResponse?>(null);

        return Task.FromResult<BankResponse?>(new BankResponse
        {
            Authorized = Result,
            AuthorizationCode = AuthorizationCode ?? (Result is true ? "fake-auth-code" : string.Empty)
        });
    }
}
