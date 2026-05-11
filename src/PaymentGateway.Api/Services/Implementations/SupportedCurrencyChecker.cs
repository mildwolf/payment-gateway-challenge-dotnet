using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Services;

public class SupportedCurrencyChecker : ISupportedCurrencyChecker
{
    private readonly HashSet<string> _supportedCurrencies;

    public SupportedCurrencyChecker(IConfiguration configuration)
    {
        var currencies = configuration.GetSection("SupportedCurrencies").Get<string[]>()
            ?? ["USD", "EUR", "GBP"];
        _supportedCurrencies = new HashSet<string>(currencies, StringComparer.OrdinalIgnoreCase);
    }

    public SupportedCurrencyChecker(string[] currencies)
    {
        _supportedCurrencies = new HashSet<string>(currencies, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsSupported(string currencyCode)
    {
        return _supportedCurrencies.Contains(currencyCode);
    }
}
