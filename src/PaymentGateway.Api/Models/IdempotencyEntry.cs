using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Models;

public enum IdempotencyStatus
{
    InFlight,
    Completed
}

public class IdempotencyEntry
{
    public IdempotencyStatus Status { get; set; }
    public PostPaymentResponse? Response { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
