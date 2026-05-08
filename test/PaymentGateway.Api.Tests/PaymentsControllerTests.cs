using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();

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

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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
        var paymentResponse = await response.Content.ReadFromJsonAsync<GetPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(payment.Id, paymentResponse.Id);
        Assert.Equal(payment.CardNumberLastFour, paymentResponse.CardNumberLastFour);
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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

    [Fact]
    public async Task PostPayment_ReturnsRejected_WhenValidationFails()
    {
        // Arrange
        var fakeBank = new FakeBankService();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse.Status);
        Assert.Equal(Guid.Empty, paymentResponse.Id);
    }

    [Fact]
    public async Task PostPayment_ReturnsAuthorized_WhenBankAuthorizes()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse.Status);
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
        Assert.Equal("1111", paymentResponse.CardNumberLastFour);
    }

    [Fact]
    public async Task PostPayment_ReturnsDeclined_WhenBankDeclines()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = false };

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Declined, paymentResponse.Status);
    }

    [Fact]
    public async Task PostPayment_ReturnsDeclined_WhenBankIsUnavailable()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = null };  // Simulates bank being unreachable

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Declined, paymentResponse.Status);
    }

    [Fact]
    public async Task PostThenGet_ReturnsSamePayment()
    {
        // Arrange
        var fakeBank = new FakeBankService { Result = true };

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
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
        var postPayment = await postResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - GET
        var getResponse = await client.GetAsync($"/api/Payments/{postPayment!.Id}");
        var getPayment = await getResponse.Content.ReadFromJsonAsync<GetPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(getPayment);
        Assert.Equal(postPayment.Id, getPayment.Id);
        Assert.Equal(PaymentStatus.Authorized, getPayment.Status);
        Assert.Equal("0001", getPayment.CardNumberLastFour);
        Assert.Equal(7500, getPayment.Amount);
        Assert.Equal("GBP", getPayment.Currency);
    }
}
