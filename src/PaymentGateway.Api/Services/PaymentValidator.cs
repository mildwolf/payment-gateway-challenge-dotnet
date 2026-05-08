using System.Text.RegularExpressions;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class PaymentValidator
{
    public List<string> Validate(PostPaymentRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.CardNumber) || !Regex.IsMatch(request.CardNumber, @"^\d{14,19}$"))
            errors.Add("CardNumber must be 14-19 digits");

        if (request.ExpiryMonth < 1 || request.ExpiryMonth > 12)
            errors.Add("ExpiryMonth must be between 1 and 12");

        // Card is valid through the last day of the expiry month
        if (request.ExpiryYear > 0 && request.ExpiryMonth >= 1 && request.ExpiryMonth <= 12)
        {
            var expiryEnd = new DateTime(request.ExpiryYear, request.ExpiryMonth, 1).AddMonths(1);
            if (expiryEnd <= DateTime.UtcNow)
                errors.Add("Card has expired");
        }

        if (string.IsNullOrEmpty(request.Currency) || !Regex.IsMatch(request.Currency, @"^[A-Z]{3}$"))
            errors.Add("Currency must be a 3-letter ISO 4217 code");

        if (request.Amount < 0)
            errors.Add("Amount must be non-negative");

        if (string.IsNullOrEmpty(request.Cvv) || !Regex.IsMatch(request.Cvv, @"^\d{3,4}$"))
            errors.Add("CVV must be 3-4 digits");

        return errors;
    }
}
