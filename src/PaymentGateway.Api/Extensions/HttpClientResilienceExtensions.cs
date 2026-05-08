using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace PaymentGateway.Api.Extensions;

public static class HttpClientResilienceExtensions
{
    public static IHttpClientBuilder AddBankResiliencePipeline(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("bank-pipeline", static builder =>
        {
            // Outer: total timeout for the entire pipeline (all attempts combined)
            builder.AddTimeout(TimeSpan.FromSeconds(30));

            // Middle: retry transient failures
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            });

            // Inner: per-request timeout
            builder.AddTimeout(TimeSpan.FromSeconds(5));
        });

        return builder;
    }
}
