using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services.Contracts;

public interface IPaymentsRepository
{
    void Add(PostPaymentResponse payment);
    GetPaymentResponse? Get(Guid id);
}
