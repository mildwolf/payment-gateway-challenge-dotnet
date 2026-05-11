namespace PaymentGateway.Api.Services.Contracts;

public interface ISupportedCurrencyChecker
{
    bool IsSupported(string currencyCode);
}
