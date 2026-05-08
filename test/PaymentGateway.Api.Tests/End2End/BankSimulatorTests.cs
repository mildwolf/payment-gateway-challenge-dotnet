using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Tests.End2End;

[Trait("Category", "End2End")]
public class BankSimulatorTests
{
    private static HttpClient CreateClient()
    {
        var factory = new WebApplicationFactory<Program>();
        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                ((ServiceCollection)services).AddHttpClient<IBankService, BankService>(c =>
                    c.BaseAddress = new Uri("http://localhost:8080"));
            }))
            .CreateClient();
    }

    // Card ending in 1 (odd) → bank returns authorized=true with authorization_code
    [E2EFact]
    public async Task PostPayment_CardEndsOdd1_ReturnsAuthorized()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111111");

        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.NotEmpty(payment.AuthorizationCode);
    }

    // Card ending in 3 (odd) → bank returns authorized=true with authorization_code
    [E2EFact]
    public async Task PostPayment_CardEndsOdd3_ReturnsAuthorized()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111113");

        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Authorized, payment!.Status);
        Assert.NotEmpty(payment.AuthorizationCode);
    }

    // Card ending in 2 (even) → bank returns authorized=false, empty authorization_code
    [E2EFact]
    public async Task PostPayment_CardEndsEven2_ReturnsDeclined()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111112");

        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
        Assert.Equal(string.Empty, payment.AuthorizationCode);
    }

    // Card ending in 4 (even) → bank returns authorized=false, empty authorization_code
    [E2EFact]
    public async Task PostPayment_CardEndsEven4_ReturnsDeclined()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111114");

        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
        Assert.Equal(string.Empty, payment.AuthorizationCode);
    }

    // Card ending in 0 → bank returns 503, gateway treats as Declined
    [E2EFact]
    public async Task PostPayment_CardEndsZero_ReturnsDeclined()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111110");

        var response = await client.PostAsJsonAsync("/api/Payments", request);
        var payment = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Declined, payment!.Status);
    }

    // Authorized payment can be retrieved via GET with all fields intact
    [E2EFact]
    public async Task PostThenGet_CardEndsOdd_DataIntact()
    {
        var client = CreateClient();
        var request = ValidRequest("5500000000000001");

        var postResponse = await client.PostAsJsonAsync("/api/Payments", request);
        var postPayment = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(PaymentStatus.Authorized, postPayment!.Status);

        var getResponse = await client.GetAsync($"/api/Payments/{postPayment.Id}");
        var getPayment = await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getPayment);
        Assert.Equal(postPayment.Id, getPayment!.Id);
        Assert.Equal(PaymentStatus.Authorized, getPayment.Status);
        Assert.Equal("0001", getPayment.CardNumberLastFour);
        Assert.Equal(request.Amount, getPayment.Amount);
        Assert.Equal(request.Currency, getPayment.Currency);
    }

    // Declined payment can be retrieved via GET with all fields intact
    [E2EFact]
    public async Task PostThenGet_CardEndsEven_DataIntact()
    {
        var client = CreateClient();
        var request = ValidRequest("5500000000000002");

        var postResponse = await client.PostAsJsonAsync("/api/Payments", request);
        var postPayment = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(PaymentStatus.Declined, postPayment!.Status);

        var getResponse = await client.GetAsync($"/api/Payments/{postPayment.Id}");
        var getPayment = await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getPayment);
        Assert.Equal(postPayment.Id, getPayment!.Id);
        Assert.Equal(PaymentStatus.Declined, getPayment.Status);
        Assert.Equal("0002", getPayment.CardNumberLastFour);
    }

    // Same Idempotency-Key sent twice returns the same payment (same ID, same status)
    [E2EFact]
    public async Task PostWithIdempotencyKey_DuplicateRequest_ReturnsSamePayment()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111111");

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        firstRequest.Headers.Add("Idempotency-Key", "e2e-idem-key-1");

        var firstResponse = await client.SendAsync(firstRequest);
        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        secondRequest.Headers.Add("Idempotency-Key", "e2e-idem-key-1");

        var secondResponse = await client.SendAsync(secondRequest);
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(firstPayment);
        Assert.NotNull(secondPayment);
        Assert.Equal(firstPayment!.Id, secondPayment!.Id);
        Assert.Equal(firstPayment.Status, secondPayment.Status);
    }

    // Different Idempotency-Key values create separate payments
    [E2EFact]
    public async Task PostWithDifferentIdempotencyKeys_CreatesDifferentPayments()
    {
        var client = CreateClient();
        var request = ValidRequest("4111111111111111");

        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        firstRequest.Headers.Add("Idempotency-Key", "e2e-key-a");

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(request)
        };
        secondRequest.Headers.Add("Idempotency-Key", "e2e-key-b");

        var firstResponse = await client.SendAsync(firstRequest);
        var secondResponse = await client.SendAsync(secondRequest);

        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.NotEqual(firstPayment!.Id, secondPayment!.Id);
    }

    private static PostPaymentRequest ValidRequest(string cardNumber) => new()
    {
        CardNumber = cardNumber,
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 1000,
        Cvv = "123"
    };
}

// Custom fact attribute: skips tests when bank simulator is unreachable,
// with a clear message explaining how to start it.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!BankSimulatorAvailability.IsAvailable)
        {
            Skip = "Bank simulator is not running on http://localhost:8080. " +
                   "Start it with: docker-compose up";
        }
    }
}

// One-time check for simulator availability at test discovery time
public static class BankSimulatorAvailability
{
    public static bool IsAvailable { get; }
    public static string? ErrorMessage { get; }

    static BankSimulatorAvailability()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var task = http.PostAsync("http://localhost:8080/payments",
                JsonContent.Create(new { card_number = "0000000000000001", expiry_date = "12/30", currency = "GBP", amount = 1, cvv = "123" }));
            task.Wait();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            ErrorMessage = ex.InnerException?.Message ?? ex.Message;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine("  E2E TESTS SKIPPED: Bank simulator is not running");
            Console.WriteLine("  Endpoint: http://localhost:8080/payments");
            Console.WriteLine($"  Error: {ErrorMessage}");
            Console.WriteLine("  To start the simulator, run:");
            Console.WriteLine("    docker-compose up");
            Console.WriteLine("============================================================");
            Console.WriteLine();
            Console.ResetColor();
        }
    }
}
