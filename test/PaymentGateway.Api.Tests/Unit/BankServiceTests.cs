using System.Net;
using System.Text;
using System.Text.Json;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Tests.Unit;

[Trait("Category", "Unit")]
public class BankServiceTests
{
    private static PostPaymentRequest TestRequest() => new()
    {
        CardNumber = "4111111111111111",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1000,
        Cvv = "123"
    };

    // Verifies that when the bank responds with authorized=true,
    // ProcessPaymentAsync returns a BankResponse with Authorized=true and the authorization code.
    [Fact]
    public async Task ProcessPaymentAsync_Authorized_ReturnsAuthorizedWithCode()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { authorized = true, authorization_code = "abc-123" }));

        var service = new BankService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") });

        var result = await service.ProcessPaymentAsync(TestRequest());

        Assert.NotNull(result);
        Assert.True(result!.Authorized);
        Assert.Equal("abc-123", result.AuthorizationCode);
    }

    // Verifies that when the bank responds with authorized=false,
    // ProcessPaymentAsync returns a BankResponse with Authorized=false and empty authorization code.
    [Fact]
    public async Task ProcessPaymentAsync_Declined_ReturnsDeclinedWithEmptyCode()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { authorized = false, authorization_code = "" }));

        var service = new BankService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") });

        var result = await service.ProcessPaymentAsync(TestRequest());

        Assert.NotNull(result);
        Assert.False(result!.Authorized);
        Assert.Equal(string.Empty, result!.AuthorizationCode);
    }

    // Verifies that when the bank returns 503 Service Unavailable,
    // ProcessPaymentAsync returns a BankResponse with Authorized=false, keeping the gateway
    // available even when the bank is down.
    [Fact]
    public async Task ProcessPaymentAsync_ServiceUnavailable_ReturnsDeclined()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");

        var service = new BankService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") });

        var result = await service.ProcessPaymentAsync(TestRequest());

        Assert.NotNull(result);
        Assert.False(result!.Authorized);
    }

    // Verifies that when the bank is completely unreachable (network error),
    // ProcessPaymentAsync returns null so the controller can distinguish this
    // from a normal declined response if needed.
    [Fact]
    public async Task ProcessPaymentAsync_NetworkError_ReturnsNull()
    {
        var handler = new MockErrorHttpMessageHandler();
        var service = new BankService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") });

        var result = await service.ProcessPaymentAsync(TestRequest());

        Assert.Null(result);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }

    private class MockErrorHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated network error");
        }
    }
}
