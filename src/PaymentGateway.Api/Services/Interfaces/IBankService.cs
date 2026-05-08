using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public interface IBankService
{
    Task<bool?> ProcessPaymentAsync(PostPaymentRequest request);
}
