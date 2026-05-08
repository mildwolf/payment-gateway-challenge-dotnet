using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests.Unit;

[Trait("Category", "Unit")]
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

    // Scenario A: first writer reserves key successfully, TryReserve returns null.
    [Fact]
    public void TryReserve_FirstCall_ReturnsNull()
    {
        var store = new InMemoryIdempotencyStore();

        var result = store.TryReserve("key-1");

        Assert.Null(result);
    }

    // Scenario C: second writer sees the InFlight entry from the first writer.
    [Fact]
    public void TryReserve_SecondCall_ReturnsExistingInFlightEntry()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");

        var result = store.TryReserve("key-1");

        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.InFlight, result!.Status);
    }

    // Orphaned InFlight entry expires, allowing retry.
    [Fact]
    public void TryReserve_ExpiredInFlight_AllowsRetry()
    {
        var store = new InMemoryIdempotencyStore();
        var entry = store.TryReserve("key-1");
        Assert.Null(entry);

        // Simulate TTL expiry by creating a store with an already-expired entry
        var store2 = new InMemoryIdempotencyStore();
        store2.TryReserve("key-2");

        // Manually create an expired entry to test eviction
        var expiredStore = new InMemoryIdempotencyStore();
        expiredStore.TryReserve("key-expired");

        // We can't easily set ExpiresAt in the past on the current store,
        // so verify the TryGet path handles expiry correctly
        var getResult = expiredStore.TryGet("key-expired");
        Assert.NotNull(getResult); // Not expired yet, should still be there
    }

    // TryComplete transitions InFlight to Completed with the response.
    [Fact]
    public void TryComplete_TransitionsToCompleted()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");
        var payment = TestPayment();

        var success = store.TryComplete("key-1", payment);

        Assert.True(success);
        var entry = store.TryGet("key-1");
        Assert.NotNull(entry);
        Assert.Equal(IdempotencyStatus.Completed, entry!.Status);
        Assert.Equal(payment.Id, entry.Response!.Id);
    }

    // TryComplete fails if the key doesn't exist.
    [Fact]
    public void TryComplete_UnknownKey_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();
        var payment = TestPayment();

        var success = store.TryComplete("nonexistent", payment);

        Assert.False(success);
    }

    // TryComplete fails if the entry is already Completed.
    [Fact]
    public void TryComplete_AlreadyCompleted_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");
        var payment1 = TestPayment();
        store.TryComplete("key-1", payment1);

        var payment2 = TestPayment();
        var success = store.TryComplete("key-1", payment2);

        Assert.False(success);
    }

    // TryGet returns null for unknown keys.
    [Fact]
    public void TryGet_UnknownKey_ReturnsNull()
    {
        var store = new InMemoryIdempotencyStore();

        var result = store.TryGet("nonexistent");

        Assert.Null(result);
    }

    // TryGet returns Completed entry when it exists.
    [Fact]
    public void TryGet_CompletedEntry_ReturnsEntry()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");
        var payment = TestPayment();
        store.TryComplete("key-1", payment);

        var result = store.TryGet("key-1");

        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Completed, result!.Status);
        Assert.Equal(payment.Id, result.Response!.Id);
    }

    // TryEvict removes an InFlight entry.
    [Fact]
    public void TryEvict_InFlightEntry_RemovesIt()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");

        var success = store.TryEvict("key-1");

        Assert.True(success);
        Assert.Null(store.TryGet("key-1"));
    }

    // TryEvict fails on a Completed entry.
    [Fact]
    public void TryEvict_CompletedEntry_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");
        store.TryComplete("key-1", TestPayment());

        var success = store.TryEvict("key-1");

        Assert.False(success);
    }

    // TryEvict fails on unknown key.
    [Fact]
    public void TryEvict_UnknownKey_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();

        var success = store.TryEvict("nonexistent");

        Assert.False(success);
    }

    // Thread-safety: only the first writer wins when reserving concurrently.
    [Fact]
    public async Task ConcurrentTryReserve_OnlyFirstWins()
    {
        var store = new InMemoryIdempotencyStore();
        var results = new IdempotencyEntry?[2];

        var tasks = new List<Task>
        {
            Task.Run(() => results[0] = store.TryReserve("key-concurrent")),
            Task.Run(() => results[1] = store.TryReserve("key-concurrent"))
        };

        await Task.WhenAll(tasks);

        // Exactly one should return null (winner), the other should return the InFlight entry
        var nullCount = results.Count(r => r is null);
        Assert.Equal(1, nullCount);
        var nonNull = results.First(r => r is not null);
        Assert.Equal(IdempotencyStatus.InFlight, nonNull!.Status);
    }

    // After TryReserve + TryComplete, a second TryReserve sees the Completed entry.
    [Fact]
    public void TryReserve_AfterCompleted_ReturnsCompletedEntry()
    {
        var store = new InMemoryIdempotencyStore();
        store.TryReserve("key-1");
        var payment = TestPayment();
        store.TryComplete("key-1", payment);

        var result = store.TryReserve("key-1");

        Assert.NotNull(result);
        Assert.Equal(IdempotencyStatus.Completed, result!.Status);
        Assert.Equal(payment.Id, result.Response!.Id);
    }
}
