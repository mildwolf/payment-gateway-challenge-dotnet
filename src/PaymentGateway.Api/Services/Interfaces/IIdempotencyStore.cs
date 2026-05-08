using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public interface IIdempotencyStore
{
    PostPaymentResponse? TryGet(string idempotencyKey);
    void TryAdd(string idempotencyKey, PostPaymentResponse payment);
}
