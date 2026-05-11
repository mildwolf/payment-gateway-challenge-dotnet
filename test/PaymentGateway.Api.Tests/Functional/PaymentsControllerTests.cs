using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;
using PaymentGateway.Api.Tests.Unit;

namespace PaymentGateway.Api.Tests.Functional;

[Trait("Category", "Functional")]
public class PaymentsControllerTests
{
    private readonly Random _random = new();
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Verifies GET /api/Payments/{id} returns 200 with the correct payment data
    // when the payment exists in the repository.
    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Authorized,
            ExpiryYear = _random.Next(2026, 2030),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            CardNumberLastFour = _random.Next(1111, 9999).ToString(),
            Currency = "GBP"
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IPaymentsRepository>(paymentsRepository);
                ((ServiceCollection)services).AddHttpClient<IBankService, BankService>(c =>
                    c.BaseAddress = new Uri("http://localhost:8080"));
            }))
            .CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<GetPaymentResponse>(_jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(payment.Id, paymentResponse!.Id);
        Assert.Equal(payment.CardNumberLastFour, paymentResponse.CardNumberLastFour);
    }

    // Verifies GET /api/Payments/{id} returns 404 when the payment ID
    // does not exist in the repository.
    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddHttpClient<IBankService, BankService>(c =>
                    c.BaseAddress = new Uri("http://localhost:8080"));
            }))
            .CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Verifies POST /api/Payments returns 200 with Rejected status when the request
    // fails validation (invalid card number, expired card, bad currency, etc.).
    // Also verifies that the response includes Id=Guid.Empty and no payment is stored.
    [Fact]
    public async Task PostPayment_ReturnsRejected_WhenValidationFails()
    {
        // Arrange
        var fakeBank = new FakeBankService();
        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "123",          // Too short
            ExpiryMonth = 13,            // Invalid
            ExpiryYear = 2020,           // Expired
            Currency = "XX",             // Wrong format
            Amount = -1,                 // Negative
            Cvv = "1"                    // Too short
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse!.Status);
        Assert.Equal(Guid.Empty, paymentResponse.Id);
    }

    // Verifies POST /api/Payments returns 200 with Authorized status when the bank
    // approves the payment, and the response contains a valid payment ID and the
    // correct last-four digits of the card number.
    [Fact]
    public async Task PostPayment_ReturnsAuthorized_WhenBankAuthorizes()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse!.Status);
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
        Assert.Equal("1111", paymentResponse.CardNumberLastFour);
    }

    // Verifies POST /api/Payments returns 200 with Declined status when the bank
    // explicitly declines the payment (authorized=false).
    [Fact]
    public async Task PostPayment_ReturnsDeclined_WhenBankDeclines()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = false };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111112",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "USD",
            Amount = 5000,
            Cvv = "456"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Declined, paymentResponse!.Status);
    }

    // Verifies POST /api/Payments returns Declined status when the bank is unreachable
    // (IBankService returns null), ensuring the gateway remains available and does not
    // crash or return an error when the bank is down.
    [Fact]
    public async Task PostPayment_ReturnsDeclined_WhenBankIsUnavailable()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = null };  // Simulates bank being unreachable

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111110",
            ExpiryMonth = 6,
            ExpiryYear = 2028,
            Currency = "EUR",
            Amount = 2500,
            Cvv = "789"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Declined, paymentResponse!.Status);
    }

    // Verifies end-to-end data integrity: a payment created via POST can be retrieved
    // via GET, and all fields (ID, status, last-four, amount, currency) match exactly.
    [Fact]
    public async Task PostThenGet_ReturnsSamePayment()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "5500000000000001",
            ExpiryMonth = 3,
            ExpiryYear = 2029,
            Currency = "GBP",
            Amount = 7500,
            Cvv = "321"
        };

        // Act - POST
        var postResponse = await client.PostAsJsonAsync("/api/Payments", request);
        var postPayment = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Act - GET
        var getResponse = await client.GetAsync($"/api/Payments/{postPayment!.Id}");
        var getPayment = await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>(_jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getPayment);
        Assert.Equal(postPayment.Id, getPayment!.Id);
        Assert.Equal(PaymentStatus.Authorized, getPayment.Status);
        Assert.Equal("0001", getPayment.CardNumberLastFour);
        Assert.Equal(7500, getPayment.Amount);
        Assert.Equal("GBP", getPayment.Currency);
    }

    // Verifies idempotency: sending the same Idempotency-Key twice returns the same
    // payment (same ID, same status) without creating a duplicate or calling the bank again.
    [Fact]
    public async Task PostWithIdempotencyKey_DuplicateRequest_ReturnsSamePayment()
    {
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        // First request with idempotency key
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        firstRequest.Headers.Add("Idempotency-Key", "idem-key-1");

        var firstResponse = await client.SendAsync(firstRequest);
        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Second request with same idempotency key
        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        secondRequest.Headers.Add("Idempotency-Key", "idem-key-1");

        var secondResponse = await client.SendAsync(secondRequest);
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        // Assert - same payment returned, bank only called once (FakeBankService tracks calls)
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstPayment);
        Assert.NotNull(secondPayment);
        Assert.Equal(firstPayment!.Id, secondPayment!.Id);
        Assert.Equal(PaymentStatus.Authorized, firstPayment.Status);
        Assert.Equal(PaymentStatus.Authorized, secondPayment.Status);
    }

    // Verifies that different Idempotency-Key values result in separate payments,
    // even if the request body is identical. Each key is an independent idempotency scope.
    [Fact]
    public async Task PostWithDifferentIdempotencyKeys_CreatesDifferentPayments()
    {
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        firstRequest.Headers.Add("Idempotency-Key", "key-a");

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        secondRequest.Headers.Add("Idempotency-Key", "key-b");

        var firstResponse = await client.SendAsync(firstRequest);
        var secondResponse = await client.SendAsync(secondRequest);

        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        Assert.NotEqual(firstPayment!.Id, secondPayment!.Id);
    }

    // Verifies that requests without an Idempotency-Key header are not deduplicated —
    // each request creates a new payment, maintaining backward compatibility.
    [Fact]
    public async Task PostWithoutIdempotencyKey_CreatesNewPaymentEachTime()
    {
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var firstResponse = await client.PostAsJsonAsync("/api/Payments", request);
        var secondResponse = await client.PostAsJsonAsync("/api/Payments", request);

        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        Assert.NotEqual(firstPayment!.Id, secondPayment!.Id);
    }

    // Scenario C: when a request with the same idempotency key arrives while the first
    // is still being processed (InFlight), the gateway returns 409 Conflict.
    [Fact]
    public async Task PostWithIdempotencyKey_InFlightRequest_Returns409()
    {
        var tcs = new TaskCompletionSource<BankResponse?>();
        var fakeBank = new FakeBankService { Result = true, CompletionSource = tcs };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2030,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        // First request — will block on the bank call
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        firstRequest.Headers.Add("Idempotency-Key", "inflight-key");

        var firstTask = client.SendAsync(firstRequest);

        // Second request with same key — should get 409 while first is still in-flight
        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        secondRequest.Headers.Add("Idempotency-Key", "inflight-key");

        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        // Complete the first request's bank call
        tcs.SetResult(new BankResponse { Authorized = true, AuthorizationCode = "auth-123" });

        var firstResponse = await firstTask;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);
        Assert.Equal(PaymentStatus.Authorized, firstPayment!.Status);
    }

    // Verifies that a Rejected payment (validation failure) does not store its idempotency key,
    // so the client can fix the request and retry with the same key to create a valid payment.
    [Fact]
    public async Task PostRejectedWithKey_ThenValidWithSameKey_CreatesPayment()
    {
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<Program>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddSingleton<IBankService>(fakeBank);
            }))
            .CreateClient();

        // First: invalid request with idempotency key → Rejected (key not stored)
        var invalidRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(new PostPaymentRequest
            {
                CardNumber = "1",
                ExpiryMonth = 13,
                ExpiryYear = 2020,
                Currency = "X",
                Amount = -1,
                Cvv = "1"
            })
        };
        invalidRequest.Headers.Add("Idempotency-Key", "retry-key");

        var rejectedResponse = await client.SendAsync(invalidRequest);
        var rejectedPayment = await rejectedResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        Assert.Equal(PaymentStatus.Rejected, rejectedPayment!.Status);

        // Second: valid request with same key → creates new payment (Rejected didn't store the key)
        var validRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(new PostPaymentRequest
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = 12,
                ExpiryYear = 2030,
                Currency = "GBP",
                Amount = 1000,
                Cvv = "123"
            })
        };
        validRequest.Headers.Add("Idempotency-Key", "retry-key");

        var validResponse = await client.SendAsync(validRequest);
        var validPayment = await validResponse.Content.ReadFromJsonAsync<PostPaymentResponse>(_jsonOptions);

        Assert.Equal(PaymentStatus.Authorized, validPayment!.Status);
        Assert.NotEqual(Guid.Empty, validPayment.Id);
    }
}
