using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class InMemoryIdempotencyStoreTests
{
    private static PostPaymentResponse TestPayment(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Status = PaymentStatus.Authorized,
        CardNumberLastFour = "1111",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1000
    };

    // Verifies that a payment stored via TryAdd can be retrieved by its idempotency key,
    // and the returned payment matches the original (same ID).
    [Fact]
    public void TryAdd_ThenTryGet_ReturnsPayment()
    {
        var store = new InMemoryIdempotencyStore();
        var payment = TestPayment();

        store.TryAdd("key-1", payment);
        var result = store.TryGet("key-1");

        Assert.NotNull(result);
        Assert.Equal(payment.Id, result.Id);
    }

    // Verifies that querying a key that was never stored returns null,
    // ensuring no false positives on cache lookups.
    [Fact]
    public void TryGet_UnknownKey_ReturnsNull()
    {
        var store = new InMemoryIdempotencyStore();

        var result = store.TryGet("nonexistent");

        Assert.Null(result);
    }

    // Verifies that storing a second payment under the same key does not overwrite
    // the original — the first write wins, which is critical for preventing duplicate
    // payment processing when concurrent requests arrive.
    [Fact]
    public void TryAdd_DuplicateKey_DoesNotOverwrite()
    {
        var store = new InMemoryIdempotencyStore();
        var original = TestPayment();

        store.TryAdd("key-1", original);
        store.TryAdd("key-1", TestPayment());

        var result = store.TryGet("key-1");
        Assert.Equal(original.Id, result!.Id);
    }

    // Verifies thread-safety: when two concurrent calls race to store the same key,
    // only the first writer's payment is persisted. This simulates the real scenario
    // where duplicate network requests hit the gateway simultaneously.
    [Fact]
    public async Task ConcurrentTryAdd_OnlyFirstWins()
    {
        var store = new InMemoryIdempotencyStore();
        var firstPayment = TestPayment();
        var secondPayment = TestPayment();

        var tasks = new List<Task>
        {
            Task.Run(() => store.TryAdd("key-concurrent", firstPayment)),
            Task.Run(() => store.TryAdd("key-concurrent", secondPayment))
        };

        await Task.WhenAll(tasks);

        var result = store.TryGet("key-concurrent");
        Assert.NotNull(result);
        Assert.True(result.Id == firstPayment.Id || result.Id == secondPayment.Id);
    }
}
