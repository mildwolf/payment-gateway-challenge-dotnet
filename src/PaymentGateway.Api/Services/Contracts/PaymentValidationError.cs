namespace PaymentGateway.Api.Services.Contracts;

[Flags]
public enum PaymentValidationError
{
    None = 0,
    CardNumberInvalid = 1,
    ExpiryMonthInvalid = 2,
    CardExpired = 4,
    CurrencyInvalid = 8,
    AmountInvalid = 16,
    CvvInvalid = 32,
    CurrencyNotSupported = 64
}
