using System.Collections.Concurrent;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, PostPaymentResponse> _store = new();

    public PostPaymentResponse? TryGet(string idempotencyKey)
        => _store.TryGetValue(idempotencyKey, out var payment) ? payment : null;

    public void TryAdd(string idempotencyKey, PostPaymentResponse payment)
        => _store.TryAdd(idempotencyKey, payment);
}
