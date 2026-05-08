namespace PaymentGateway.Api.Services.Contracts;

public class ValidationResult
{
    public PaymentValidationError Errors { get; init; }
    public List<string> Messages { get; init; } = [];
    public bool IsValid => Errors == PaymentValidationError.None;
}
