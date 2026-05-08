using System.Collections.Concurrent;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();

    public void Add(PostPaymentResponse payment)
    {
        _payments.TryAdd(payment.Id, payment);
    }

    public GetPaymentResponse? Get(Guid id)
    {
        if (!_payments.TryGetValue(id, out var payment))
            return null;

        return new GetPaymentResponse
        {
            Id = payment.Id,
            Status = payment.Status,
            CardNumberLastFour = payment.CardNumberLastFour,
            ExpiryMonth = payment.ExpiryMonth,
            ExpiryYear = payment.ExpiryYear,
            Currency = payment.Currency,
            Amount = payment.Amount
        };
    }
}
