using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Tests.Unit;

[Trait("Category", "Unit")]
public class PaymentValidatorTests
{
    private readonly PaymentValidator _validator = new();

    private PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "4111111111111111",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1000,
        Cvv = "123"
    };

    // Verifies that a fully valid payment request passes all validation rules.
    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.True(result.IsValid);
        Assert.Equal(PaymentValidationError.None, result.Errors);
        Assert.Empty(result.Messages);
    }

    // Verifies that a 14-digit card number (minimum valid length) is accepted.
    [Fact]
    public void Validate_CardNumber14Digits_IsValid()
    {
        var request = ValidRequest();
        request.CardNumber = "12345678901234";
        var result = _validator.Validate(ValidRequestWith(x => x.CardNumber = "12345678901234"));
        Assert.True(result.IsValid);
    }

    // Verifies that a 19-digit card number (maximum valid length) is accepted.
    [Fact]
    public void Validate_CardNumber19Digits_IsValid()
    {
        var request = ValidRequest();
        request.CardNumber = "1234567890123456789";
        var result = _validator.Validate(ValidRequestWith(x => x.CardNumber = "1234567890123456789"));
        Assert.True(result.IsValid);
    }

    // Verifies that a 13-digit card number is rejected with CardNumberInvalid flag.
    [Fact]
    public void Validate_CardNumberTooShort_HasCardNumberInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.CardNumber = "1234567890123"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CardNumberInvalid, result.Errors);
        Assert.Single(result.Messages);
        Assert.Contains("CardNumber", result.Messages[0]);
    }

    // Verifies that a 20-digit card number is rejected with CardNumberInvalid flag.
    [Fact]
    public void Validate_CardNumberTooLong_HasCardNumberInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.CardNumber = "12345678901234567890"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CardNumberInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that non-numeric characters in the card number are rejected.
    [Fact]
    public void Validate_CardNumberWithLetters_HasCardNumberInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.CardNumber = "4111abcd11111111"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CardNumberInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that an empty card number is rejected.
    [Fact]
    public void Validate_CardNumberEmpty_HasCardNumberInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.CardNumber = ""));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CardNumberInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that expiry month 0 is rejected with ExpiryMonthInvalid flag.
    [Fact]
    public void Validate_ExpiryMonthZero_HasExpiryMonthInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.ExpiryMonth = 0));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.ExpiryMonthInvalid, result.Errors);
        Assert.Single(result.Messages);
        Assert.Contains("ExpiryMonth", result.Messages[0]);
    }

    // Verifies that expiry month 13 is rejected with ExpiryMonthInvalid flag.
    [Fact]
    public void Validate_ExpiryMonthThirteen_HasExpiryMonthInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.ExpiryMonth = 13));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.ExpiryMonthInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that a past expiry date is rejected with CardExpired flag.
    [Fact]
    public void Validate_ExpiredCard_HasCardExpired()
    {
        var result = _validator.Validate(ValidRequestWith(x =>
        {
            x.ExpiryMonth = 1;
            x.ExpiryYear = 2020;
        }));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CardExpired, result.Errors);
        Assert.Single(result.Messages);
        Assert.Contains("expired", result.Messages[0]);
    }

    // Verifies that a card expiring in the current month is still valid.
    [Fact]
    public void Validate_CardExpiresThisMonth_IsValid()
    {
        var result = _validator.Validate(ValidRequestWith(x =>
        {
            x.ExpiryMonth = DateTime.UtcNow.Month;
            x.ExpiryYear = DateTime.UtcNow.Year;
        }));
        Assert.True(result.IsValid);
    }

    // Verifies that a lowercase currency code is rejected with CurrencyInvalid flag.
    [Fact]
    public void Validate_CurrencyLowercase_HasCurrencyInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Currency = "gbp"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CurrencyInvalid, result.Errors);
        Assert.Single(result.Messages);
        Assert.Contains("Currency", result.Messages[0]);
    }

    // Verifies that a 2-character currency code is rejected.
    [Fact]
    public void Validate_CurrencyTwoChars_HasCurrencyInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Currency = "GB"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CurrencyInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that a numeric currency code is rejected.
    [Fact]
    public void Validate_CurrencyNumeric_HasCurrencyInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Currency = "123"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CurrencyInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that a negative amount is rejected with AmountInvalid flag.
    [Fact]
    public void Validate_NegativeAmount_HasAmountInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Amount = -1));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.AmountInvalid, result.Errors);
        Assert.Single(result.Messages);
        Assert.Contains("Amount", result.Messages[0]);
    }

    // Verifies that a zero amount is accepted.
    [Fact]
    public void Validate_ZeroAmount_IsValid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Amount = 0));
        Assert.True(result.IsValid);
    }

    // Verifies that a 3-digit CVV (minimum valid length) is accepted.
    [Fact]
    public void Validate_CvvThreeDigits_IsValid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Cvv = "123"));
        Assert.True(result.IsValid);
    }

    // Verifies that a 4-digit CVV (maximum valid length) is accepted.
    [Fact]
    public void Validate_CvvFourDigits_IsValid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Cvv = "1234"));
        Assert.True(result.IsValid);
    }

    // Verifies that a 2-digit CVV is rejected with CvvInvalid flag.
    [Fact]
    public void Validate_CvvTwoDigits_HasCvvInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Cvv = "12"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CvvInvalid, result.Errors);
        Assert.Single(result.Messages);
        Assert.Contains("CVV", result.Messages[0]);
    }

    // Verifies that a 5-digit CVV is rejected.
    [Fact]
    public void Validate_CvvFiveDigits_HasCvvInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Cvv = "12345"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CvvInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that non-numeric characters in the CVV are rejected.
    [Fact]
    public void Validate_CvvWithLetters_HasCvvInvalid()
    {
        var result = _validator.Validate(ValidRequestWith(x => x.Cvv = "12a"));
        Assert.False(result.IsValid);
        Assert.Equal(PaymentValidationError.CvvInvalid, result.Errors);
        Assert.Single(result.Messages);
    }

    // Verifies that when multiple fields are invalid, all corresponding error flags and
    // messages are returned so the client can fix all issues in one retry.
    [Fact]
    public void Validate_MultipleErrors_ReturnsAllErrorFlags()
    {
        var result = _validator.Validate(ValidRequestWith(x =>
        {
            x.CardNumber = "12";
            x.Currency = "X";
            x.Cvv = "1";
        }));

        Assert.False(result.IsValid);
        Assert.Equal(
            PaymentValidationError.CardNumberInvalid |
            PaymentValidationError.CurrencyInvalid |
            PaymentValidationError.CvvInvalid,
            result.Errors);
        Assert.Equal(3, result.Messages.Count);
    }

    private PostPaymentRequest ValidRequestWith(Action<PostPaymentRequest> configure)
    {
        var request = ValidRequest();
        configure(request);
        return request;
    }
}
