using System.Net;
using System.Text.Json.Serialization;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Services;

public class BankService : IBankService
{
    private readonly HttpClient _httpClient;

    public BankService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BankResponse?> ProcessPaymentAsync(PostPaymentRequest request)
    {
        var bankPayload = new BankRequest
        {
            CardNumber = request.CardNumber,
            ExpiryDate = $"{request.ExpiryMonth:D2}/{request.ExpiryYear % 100:D2}",
            Currency = request.Currency,
            Amount = request.Amount,
            Cvv = request.Cvv
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/payments", bankPayload);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return new BankResponse { Authorized = false };

            if (!response.IsSuccessStatusCode)
                return null;

            var bankResponse = await response.Content.ReadFromJsonAsync<BankSimulatorResponse>();
            if (bankResponse is null)
                return null;

            return new BankResponse
            {
                Authorized = bankResponse.Authorized,
                AuthorizationCode = bankResponse.AuthorizationCode
            };
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
