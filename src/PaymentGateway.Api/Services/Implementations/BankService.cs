using System.Net;
using System.Text.Json.Serialization;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Services;

public class BankService : IBankService
{
    private readonly HttpClient _httpClient;

    public BankService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool?> ProcessPaymentAsync(PostPaymentRequest request)
    {
        var bankPayload = new
        {
            card_number = request.CardNumber,
            expiry_date = $"{request.ExpiryMonth:D2}/{request.ExpiryYear % 100:D2}",
            currency = request.Currency,
            amount = request.Amount,
            cvv = request.Cvv
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/payments", bankPayload);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return false;

            if (!response.IsSuccessStatusCode)
                return null;

            var bankResponse = await response.Content.ReadFromJsonAsync<BankSimulatorResponse>();
            return bankResponse?.Authorized;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private record BankSimulatorResponse
    {
        [JsonPropertyName("authorized")]
        public bool Authorized { get; init; }

        [JsonPropertyName("authorization_code")]
        public string AuthorizationCode { get; init; } = string.Empty;
    }
}
