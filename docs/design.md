# Payment Gateway Design Decisions

## Overview & Assumptions

- Single acquiring bank — the gateway processes payments through one upstream bank endpoint.
- In-memory storage — `PaymentsRepository` and `IdempotencyStore` are `ConcurrentDictionary`-backed singletons; data is lost on process restart. Sufficient for the interview scope; a production system would use a persistent store.
- The bank simulator follows a convention: cards ending in an odd digit are authorized, even digits are declined, zero triggers 503.
- Authentication/authorization is out of scope — no API key or merchant identity in the current implementation.
- HTTPS termination is assumed at the infrastructure level (load balancer / reverse proxy); the gateway itself redirects HTTP to HTTPS via `UseHttpsRedirection`.

## Functional Requirements

### POST /api/payments

Accepts a payment request, validates input, forwards to the acquiring bank, and returns the result.

**Input validation** (`PaymentValidator`):
- Card number: 14–19 digits
- Expiry month: 1–12, card not expired
- Currency: 3-letter ISO 4217 code
- Amount: non-negative
- CVV: 3–4 digits

Validation uses a `[Flags]` enum (`PaymentValidationError`) so tests can assert exact error codes rather than checking error message strings. Invalid requests return `200 OK` with `Status: Rejected`.

### GET /api/payments/{id}

Retrieves a previously submitted payment by ID. Returns `404 Not Found` if the ID doesn't exist.

### Idempotency

Requests can include an `Idempotency-Key` header to guarantee exactly-once processing semantics:
- **New key**: reserves an `InFlight` entry, processes the payment, then marks it `Completed`.
- **Completed key**: returns the stored `PostPaymentResponse` directly.
- **In-flight key**: returns `409 Conflict` — the caller should retry later.
- **Failed validation**: evicts the reserved entry so the key can be reused.

## Non-Functional Requirements

### Resilience Pipeline (Bank HTTP Calls)

The gateway uses a Polly resilience pipeline (`AddBankResiliencePipeline`) on the `HttpClient` that communicates with the bank. The pipeline is layered from outer to inner:

```
Total Timeout (30s) → Retry (3 attempts) → Per-Request Timeout (5s)
```

**Retry triggers** — the retry handler retries on the following conditions:

| Condition | What it catches | Typical cause |
|---|---|---|
| `HttpRequestException` | Network-level failure | DNS failure, TCP connection refused, TLS handshake failure, connection pool exhaustion |
| `TimeoutRejectedException` | Single request exceeds 5s | Bank server slow/hung, high network latency |
| HTTP 503 `ServiceUnavailable` | Explicit 503 response | Bank overloaded, under maintenance, upstream gateway returning 503 |

**Retry parameters**: 3 max attempts, 500ms initial delay, exponential backoff with jitter.

**Worst-case timing**: 3 attempts × 5s timeout + backoff delays ≈ 16.5s, well within the 30s total timeout.

#### Trade-offs

**What is NOT retried:**

| Status | Why it matters | Current behavior |
|---|---|---|
| HTTP 500 | Common server error, often transient | Not retried — returns `Declined` |
| HTTP 502/504 | Gateway/reverse-proxy errors, frequently transient | Not retried — `BankService` returns `null`, controller maps to `Declined` |
| HTTP 429 | Rate limiting from the bank | Not retried — no backoff-and-retry on throttle |

**Rationale for the current scope:**
- The bank simulator only produces 503 and network errors, so retry coverage is tested against known failure modes.
- 502/504 are typically injected by infrastructure (load balancer, API gateway) in front of the bank — in production, adding these to `ShouldHandle` is a low-risk, high-value change.
- 429 handling is deferred because the gateway currently has no per-merchant rate limiting or backoff strategy; adding it without a coherent throttling design could mask upstream issues.
- `HttpClient.Timeout` is set to `Timeout.InfiniteTimeSpan` (`Program.cs:16`) so that Polly's per-request timeout (5s) is the sole arbiter — avoiding a race between two timeout mechanisms.

**Production considerations:**
- Add 502 and 504 to `ShouldHandle` — these are the most common transient errors from reverse proxies.
- Consider circuit-breaker (`AddCircuitBreaker`) before retry — if the bank is down, avoids hammering it with retries that will all fail.
- Consider 429 handling with a longer backoff (e.g., read `Retry-After` header).
- Total timeout (30s) may need tuning based on SLA requirements — the caller's own timeout should be longer than this.

## Code Structure / Extensibility

### Contracts Directory (`Services/Contracts/`)

Groups all service-level contracts — interfaces and their associated data types (request/response DTOs) — in one location. When a developer needs to understand how to interact with a service, they look in one place rather than scattering across `Interfaces/`, `Models/`, and `Enums/` directories.

**Contents:**
- `IBankService`, `IPaymentsRepository`, `IIdempotencyStore` — service interfaces
- `BankRequest`, `BankResponse` — bank service DTOs
- `ValidationResult`, `PaymentValidationError` — validation contract types

### PaymentValidator: Concrete Class, Not Interface

`PaymentValidator` is a concrete class without an interface — an intentional trade-off:

- **Why no interface now**: only one implementation exists; it's a pure function (stateless, no external dependencies), so testing doesn't require mocking it; controller tests can trigger validation by sending invalid data directly.
- **When to add `IPaymentValidator`**: if multiple banks with different validation rules are supported (e.g., Bank A requires 16-digit cards, Bank B accepts 14–19). At that point, bank-specific implementations (`VisaValidator`, `MastercardValidator`) would make sense. Adding an interface later is low-cost: extract the interface, register in DI, update the controller constructor.
- **Trade-off**: YAGNI vs. future extensibility. Current choice favors simplicity; the path to an interface is straightforward if multi-bank support is needed.

### BankService: Http vs Fake (Planned)

**Current wiring** — `IBankService` is registered in `Program.cs` as the HTTP implementation:

```csharp
builder.Services.AddHttpClient<IBankService, BankService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankService:Url"] ?? "http://localhost:8080");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddBankResiliencePipeline();
```

The `BankService` maps `PostPaymentRequest` → `BankRequest`, POSTs to `/payments`, and maps the bank JSON → `BankResponse`. On 503 it returns `Authorized: false` (triggering a Declined); on other non-success it returns `null` (also Declined).

**Planned: configuration switch** — A `BankService:Provider` config key will switch between `Http` and `Fake`:

```json
{
  "BankService": {
    "Url": "http://localhost:8080",
    "Provider": "Http"   // or "Fake"
  }
}
```

A `FakeBankService` in `src/Services/Implementations/` will replicate the mountebank simulator logic (odd-ending card = authorized, even = declined, zero = unavailable), enabling development without Docker. The `FakeBankService` in `test/` (Unit folder) remains a controlled test stub where `Result` is set explicitly — serving a different purpose.

### Test Organization

Tests are split into `Unit/` and `Functional/` directories with xUnit `[Trait]` markers:

- **Unit** — no external dependencies, fast (`dotnet test --filter "Category=Unit"`)
- **Functional** — full ASP.NET Core pipeline via `WebApplicationFactory<Program>` (`dotnet test --filter "Category=Functional"`)

Functional resilience tests (`BankResilienceTests`) use a `TrackingHandler` that programs a sequence of HTTP responses/errors, then verifies that the Polly pipeline retries correctly through the full API pipeline.
