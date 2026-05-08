using System.Collections.Concurrent;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Services;

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _store = new();

    public IdempotencyEntry? TryReserve(string idempotencyKey)
    {
        var entry = new IdempotencyEntry
        {
            Status = IdempotencyStatus.InFlight,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30)
        };

        if (_store.TryAdd(idempotencyKey, entry))
            return null;

        if (_store.TryGetValue(idempotencyKey, out var existing))
        {
            if (existing.ExpiresAt.HasValue && existing.ExpiresAt < DateTimeOffset.UtcNow)
            {
                if (((ICollection<KeyValuePair<string, IdempotencyEntry>>)_store)
                    .Remove(new KeyValuePair<string, IdempotencyEntry>(idempotencyKey, existing)))
                {
                    return TryReserve(idempotencyKey);
                }

                _store.TryGetValue(idempotencyKey, out existing);
            }

            return existing;
        }

        return TryReserve(idempotencyKey);
    }

    public bool TryComplete(string idempotencyKey, PostPaymentResponse response)
    {
        if (!_store.TryGetValue(idempotencyKey, out var entry))
            return false;

        if (entry.Status != IdempotencyStatus.InFlight)
            return false;

        var completedEntry = new IdempotencyEntry
        {
            Status = IdempotencyStatus.Completed,
            Response = response,
            CreatedAt = entry.CreatedAt,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        return ((ICollection<KeyValuePair<string, IdempotencyEntry>>)_store)
            .Remove(new KeyValuePair<string, IdempotencyEntry>(idempotencyKey, entry))
            && _store.TryAdd(idempotencyKey, completedEntry);
    }

    public IdempotencyEntry? TryGet(string idempotencyKey)
    {
        if (!_store.TryGetValue(idempotencyKey, out var entry))
            return null;

        if (entry.ExpiresAt.HasValue && entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            ((ICollection<KeyValuePair<string, IdempotencyEntry>>)_store)
                .Remove(new KeyValuePair<string, IdempotencyEntry>(idempotencyKey, entry));
            return null;
        }

        return entry;
    }

    public bool TryEvict(string idempotencyKey)
    {
        if (!_store.TryGetValue(idempotencyKey, out var entry))
            return false;

        if (entry.Status != IdempotencyStatus.InFlight)
            return false;

        return ((ICollection<KeyValuePair<string, IdempotencyEntry>>)_store)
            .Remove(new KeyValuePair<string, IdempotencyEntry>(idempotencyKey, entry));
    }
}
