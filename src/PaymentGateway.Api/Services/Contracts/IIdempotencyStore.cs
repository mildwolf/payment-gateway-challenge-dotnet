using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services.Contracts;

public interface IIdempotencyStore
{
    IdempotencyEntry? TryReserve(string idempotencyKey);
    bool TryComplete(string idempotencyKey, PostPaymentResponse response);
    IdempotencyEntry? TryGet(string idempotencyKey);
    bool TryEvict(string idempotencyKey);
}
