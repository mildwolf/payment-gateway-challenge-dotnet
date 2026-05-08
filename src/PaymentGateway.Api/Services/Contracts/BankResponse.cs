namespace PaymentGateway.Api.Services.Contracts;

public class BankResponse
{
    public bool? Authorized { get; set; }
    public string AuthorizationCode { get; set; } = string.Empty;
}
