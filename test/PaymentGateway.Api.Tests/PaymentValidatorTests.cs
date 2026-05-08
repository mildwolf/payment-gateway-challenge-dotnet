using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

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

    // Verifies that a fully valid payment request passes all validation rules
    // with zero errors returned.
    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        var result = _validator.Validate(ValidRequest());
        Assert.Empty(result);
    }

    // Verifies that a card number with fewer than 14 digits is rejected.
    // PAN lengths range from 14 to 19 digits per ISO/IEC 7812.
    [Fact]
    public void Validate_CardNumberTooShort_ReturnsError()
    {
        var request = ValidRequest();
        request.CardNumber = "1234567890123";  // 13 digits
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a card number exceeding 19 digits is rejected.
    [Fact]
    public void Validate_CardNumberTooLong_ReturnsError()
    {
        var request = ValidRequest();
        request.CardNumber = "12345678901234567890";  // 20 digits
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that non-numeric characters in the card number are rejected.
    // Card numbers must be pure digits.
    [Fact]
    public void Validate_CardNumberWithLetters_ReturnsError()
    {
        var request = ValidRequest();
        request.CardNumber = "4111abcd11111111";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that an empty card number string is rejected,
    // ensuring null/empty checks run before regex matching.
    [Fact]
    public void Validate_CardNumberEmpty_ReturnsError()
    {
        var request = ValidRequest();
        request.CardNumber = "";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that expiry month 0 is rejected — months must be 1-12.
    [Fact]
    public void Validate_ExpiryMonthZero_ReturnsError()
    {
        var request = ValidRequest();
        request.ExpiryMonth = 0;
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that expiry month 13 is rejected — months must be 1-12.
    [Fact]
    public void Validate_ExpiryMonthThirteen_ReturnsError()
    {
        var request = ValidRequest();
        request.ExpiryMonth = 13;
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a card with a past expiry date is rejected.
    // A card expiring in Jan 2020 is well past the current date.
    [Fact]
    public void Validate_ExpiredCard_ReturnsError()
    {
        var request = ValidRequest();
        request.ExpiryMonth = 1;
        request.ExpiryYear = 2020;
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a card expiring in the current month is still valid,
    // since cards remain usable through the last day of the expiry month.
    [Fact]
    public void Validate_CardExpiresThisMonth_IsValid()
    {
        var request = ValidRequest();
        request.ExpiryMonth = DateTime.UtcNow.Month;
        request.ExpiryYear = DateTime.UtcNow.Year;
        Assert.Empty(_validator.Validate(request));
    }

    // Verifies that lowercase currency codes are rejected — ISO 4217 requires uppercase.
    [Fact]
    public void Validate_CurrencyLowercase_ReturnsError()
    {
        var request = ValidRequest();
        request.Currency = "gbp";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a 2-character currency code is rejected — ISO 4217 is exactly 3 letters.
    [Fact]
    public void Validate_CurrencyTwoChars_ReturnsError()
    {
        var request = ValidRequest();
        request.Currency = "GB";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that numeric currency codes are rejected — must be alphabetic letters.
    [Fact]
    public void Validate_CurrencyNumeric_ReturnsError()
    {
        var request = ValidRequest();
        request.Currency = "123";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a negative payment amount is rejected — amounts must be non-negative.
    [Fact]
    public void Validate_NegativeAmount_ReturnsError()
    {
        var request = ValidRequest();
        request.Amount = -1;
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a zero amount is accepted — zero-value payments (e.g. card checks)
    // are a legitimate use case.
    [Fact]
    public void Validate_ZeroAmount_IsValid()
    {
        var request = ValidRequest();
        request.Amount = 0;
        Assert.Empty(_validator.Validate(request));
    }

    // Verifies that a 2-digit CVV is rejected — standard CVV is 3 or 4 digits.
    [Fact]
    public void Validate_CvvTwoDigits_ReturnsError()
    {
        var request = ValidRequest();
        request.Cvv = "12";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a 5-digit CVV is rejected — exceeds the 3-4 digit range.
    [Fact]
    public void Validate_CvvFiveDigits_ReturnsError()
    {
        var request = ValidRequest();
        request.Cvv = "12345";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that non-numeric characters in the CVV are rejected.
    [Fact]
    public void Validate_CvvWithLetters_ReturnsError()
    {
        var request = ValidRequest();
        request.Cvv = "12a";
        Assert.NotEmpty(_validator.Validate(request));
    }

    // Verifies that a 4-digit CVV is accepted — some card issuers (e.g. Amex) use 4-digit CVVs.
    [Fact]
    public void Validate_CvvFourDigits_IsValid()
    {
        var request = ValidRequest();
        request.Cvv = "1234";
        Assert.Empty(_validator.Validate(request));
    }

    // Verifies that when multiple fields are invalid, all corresponding errors are returned
    // (not just the first one), so the client can fix all issues in one retry.
    [Fact]
    public void Validate_MultipleErrors_ReturnsMultipleErrors()
    {
        var request = ValidRequest();
        request.CardNumber = "12";
        request.Currency = "X";
        request.Cvv = "1";

        var errors = _validator.Validate(request);
        Assert.True(errors.Count >= 3);
    }
}
