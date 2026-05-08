using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class FakeBankService : IBankService
{
    public bool? Result { get; set; }

    public Task<bool?> ProcessPaymentAsync(PostPaymentRequest request)
    {
        return Task.FromResult(Result);
    }
}
