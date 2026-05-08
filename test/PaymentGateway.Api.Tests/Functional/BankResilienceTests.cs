using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaymentGateway.Api.Extensions;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Tests.Functional;

[Trait("Category", "Functional")]
public class BankResilienceTests
{
    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "4111111111111111",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1000,
        Cvv = "123"
    };

    // Verifies that when the bank returns 503 then 200, Polly retries and the payment
    // is ultimately authorized via the full API pipeline.
    [Fact]
    public async Task Retry_On503_ThenSuccess_ReturnsAuthorized()
    {
        // Arrange
        var bank = new TrackingHandler(
            TrackingHandler.Res(503, "{}"),
            TrackingHandler.Res(200, """{"authorized":true,"authorization_code":"retry-ok"}""")
        );

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankService>();
                services.AddHttpClient<IBankService, BankService>(c =>
                {
                    c.BaseAddress = new Uri("http://localhost:8080");
                    c.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => bank)
                .AddBankResiliencePipeline();
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.Equal("retry-ok", payment.AuthorizationCode);
        Assert.Equal(2, bank.CallCount);
    }

    // Verifies that when the bank throws HttpRequestException (network error) on the first
    // attempt and succeeds on the second, Polly retries and the payment is authorized.
    [Fact]
    public async Task Retry_OnNetworkError_ThenSuccess_ReturnsAuthorized()
    {
        // Arrange
        var bank = new TrackingHandler(
            TrackingHandler.Err("Simulated network error"),
            TrackingHandler.Res(200, """{"authorized":true,"authorization_code":"net-retry"}""")
        );

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankService>();
                services.AddHttpClient<IBankService, BankService>(c =>
                {
                    c.BaseAddress = new Uri("http://localhost:8080");
                    c.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => bank)
                .AddBankResiliencePipeline();
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.Equal(2, bank.CallCount);
    }

    // Verifies that when the bank returns 503 consistently (exceeding max retry attempts),
    // the gateway returns Declined rather than crashing.
    [Fact]
    public async Task Retry_ExhaustedOn503_ReturnsDeclined()
    {
        // Arrange
        var bank = new TrackingHandler(
            TrackingHandler.Res(503, "{}"),
            TrackingHandler.Res(503, "{}"),
            TrackingHandler.Res(503, "{}"),
            TrackingHandler.Res(503, "{}")
        );

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankService>();
                services.AddHttpClient<IBankService, BankService>(c =>
                {
                    c.BaseAddress = new Uri("http://localhost:8080");
                    c.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => bank)
                .AddBankResiliencePipeline();
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
    }

    // Verifies that when the bank is completely unreachable on every attempt (network error),
    // the gateway returns Declined after exhausting retries.
    [Fact]
    public async Task Retry_ExhaustedOnNetworkError_ReturnsDeclined()
    {
        // Arrange
        var bank = new TrackingHandler(
            TrackingHandler.Err("Simulated network error"),
            TrackingHandler.Err("Simulated network error"),
            TrackingHandler.Err("Simulated network error"),
            TrackingHandler.Err("Simulated network error")
        );

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankService>();
                services.AddHttpClient<IBankService, BankService>(c =>
                {
                    c.BaseAddress = new Uri("http://localhost:8080");
                    c.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => bank)
                .AddBankResiliencePipeline();
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
    }

    // Verifies that a mix of 503 and network errors still retries through to success.
    [Fact]
    public async Task Retry_MixedFailures_ThenSuccess_ReturnsAuthorized()
    {
        // Arrange
        var bank = new TrackingHandler(
            TrackingHandler.Res(503, "{}"),
            TrackingHandler.Err("Simulated network error"),
            TrackingHandler.Res(200, """{"authorized":true,"authorization_code":"mixed-ok"}""")
        );

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBankService>();
                services.AddHttpClient<IBankService, BankService>(c =>
                {
                    c.BaseAddress = new Uri("http://localhost:8080");
                    c.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => bank)
                .AddBankResiliencePipeline();
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.Equal("mixed-ok", payment.AuthorizationCode);
        Assert.Equal(3, bank.CallCount);
    }

    // Tracks call count and returns a programmed sequence of responses or errors.
    private class TrackingHandler : HttpMessageHandler
    {
        private readonly Queue<(bool IsError, int StatusCode, string Body, string? ErrorMessage)> _queue = new();
        private (bool IsError, int StatusCode, string Body, string? ErrorMessage) _last;

        public int CallCount { get; private set; }

        public TrackingHandler(
            params (bool IsError, int StatusCode, string Body, string? ErrorMessage)[] steps)
        {
            foreach (var s in steps) _queue.Enqueue(s);
            _last = steps[^1];
        }

        public static (bool IsError, int StatusCode, string Body, string? ErrorMessage)
            Res(int statusCode, string body) => (false, statusCode, body, null);

        public static (bool IsError, int StatusCode, string Body, string? ErrorMessage)
            Err(string message) => (true, 0, "", message);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var step = _queue.Count > 0 ? _queue.Dequeue() : _last;

            if (step.IsError)
                throw new HttpRequestException(step.ErrorMessage);

            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)step.StatusCode)
            {
                Content = new StringContent(step.Body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
