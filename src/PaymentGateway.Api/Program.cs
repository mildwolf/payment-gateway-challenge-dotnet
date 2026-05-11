using System.Text.Json.Serialization;
using PaymentGateway.Api.Extensions;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddSingleton<ISupportedCurrencyChecker, SupportedCurrencyChecker>();
builder.Services.AddSingleton<PaymentValidator>();
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddHttpClient<IBankService, BankService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankService:Url"] ?? "http://localhost:8080");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddBankResiliencePipeline();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
