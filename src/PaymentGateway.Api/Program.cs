using PaymentGateway.Api.Extensions;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
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
