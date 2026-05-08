using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services.Contracts;

public interface IBankService
{
    Task<BankResponse?> ProcessPaymentAsync(PostPaymentRequest request);
}
