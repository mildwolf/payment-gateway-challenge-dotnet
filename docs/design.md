# Payment Gateway Design Decisions

## Overview & Assumptions

- Single acquiring bank -- the gateway processes payments through one upstream bank endpoint.
- In-memory storage -- `PaymentsRepository` and `IdempotencyStore` are `ConcurrentDictionary`-backed singletons; data is lost on process restart. Sufficient for the interview scope; a production system would use a persistent store.
- The bank simulator follows a convention: cards ending in an odd digit are authorized, even digits are declined, zero triggers 503.
- Authentication/authorization is out of scope -- no API key or merchant identity in the current implementation.
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
- **In-flight key**: returns `409 Conflict` -- the caller should retry later.
- **Failed validation**: evicts the reserved entry so the key can be reused.

## Non-Functional Requirements

### Resilience Pipeline (Bank HTTP Calls)

The gateway uses a Polly resilience pipeline (`AddBankResiliencePipeline`) on the `HttpClient` that communicates with the bank. The pipeline is layered from outer to inner:

```
Total Timeout (30s) → Retry (3 attempts) → Per-Request Timeout (5s)
```

**Retry triggers** -- the retry handler retries on the following conditions:

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
| HTTP 500 | Common server error, often transient | Not retried -- returns `Declined` |
| HTTP 502/504 | Gateway/reverse-proxy errors, frequently transient | Not retried -- `BankService` returns `null`, controller maps to `Declined` |
| HTTP 429 | Rate limiting from the bank | Not retried -- no backoff-and-retry on throttle |

**Rationale for the current scope:**
- The bank simulator only produces 503 and network errors, so retry coverage is tested against known failure modes.
- 502/504 are typically injected by infrastructure (load balancer, API gateway) in front of the bank -- in production, adding these to `ShouldHandle` is a low-risk, high-value change.
- 429 handling is deferred because the gateway currently has no per-merchant rate limiting or backoff strategy; adding it without a coherent throttling design could mask upstream issues.
- `HttpClient.Timeout` is set to `Timeout.InfiniteTimeSpan` (`Program.cs:16`) so that Polly's per-request timeout (5s) is the sole arbiter -- avoiding a race between two timeout mechanisms.

**Production considerations:**
- Add 502 and 504 to `ShouldHandle` -- these are the most common transient errors from reverse proxies.
- Consider circuit-breaker (`AddCircuitBreaker`) before retry -- if the bank is down, avoids hammering it with retries that will all fail.
- Consider 429 handling with a longer backoff (e.g., read `Retry-After` header).
- Total timeout (30s) may need tuning based on SLA requirements -- the caller's own timeout should be longer than this.

### Multi-Node Deployment [TODO]

The current implementation is single-node. For multi-node deployment behind a load balancer, four concerns need addressing:

**Connection draining** -- When a node needs to go offline (deployment, scaling down, overload), the LB stops routing new requests but allows in-flight requests to complete within a drain timeout (typically 30s). The gateway cooperates by tracking in-flight request count and rejecting new requests with `503` + `Connection: close` once draining starts. An `IConnectionDrainingManager` interface with an in-memory implementation will provide this state; a middleware will enforce the reject-on-drain behavior.

**Health probes** -- Readiness and liveness must be separate probes: `GET /health/live` returns 200 (process alive, don't kill) and `GET /health/ready` returns 200 or 503 (can/cannot accept traffic). A node can be live but not ready (e.g., draining, waiting for circuit-breaker recovery). The LB reads the readiness probe -- 503 means stop routing new traffic, achieving natural rebalance. Combining the two probes risks killing an overloaded node instead of just removing it from rotation, which loses in-flight requests.

**Keep-alive control** -- HTTP/1.1 keep-alive pins a client to one backend node indefinitely. The gateway should add `Keep-Alive: timeout=60, max=100` response headers and configure Kestrel's `KeepAliveTimeout` to match. The `max` parameter forces periodic reconnection, triggering LB reassignment and ensuring even load distribution.

**Cross-node throttling** *[May not required]* -- In a multi-node setup, per-node rate limiting alone causes uneven rejection: a busy node hits its limit while an idle node accepts everything. A shared throttling state (e.g., Redis-backed sliding window counter) ensures the cluster enforces a global rate limit, and the LB sees consistent 429 responses regardless of which node handles the request.

**Health probes interact with resilience pipeline** -- When the bank circuit-breaker opens (all retries exhausted, downstream still failing), the node is live but cannot process payments. The readiness probe should reflect this: if the circuit is open, `/health/ready` returns 503, causing the LB to stop routing to this node. This avoids sending requests to a node that will just decline them immediately. When the circuit closes, readiness recovers and the LB resumes routing.

### Audit Logging [TODO]

Payment operations require immutable audit records for dispute resolution and regulatory compliance. Unlike business logs, audit logs are write-once: they record what happened to which payment, when, and by whom (or by which system component), and must never be modified or deleted.

Key events to audit: payment submitted, bank response received (authorized/declined), idempotent replay, validation rejection. Each audit entry should include the payment ID, outcome, timestamp, and the idempotency key if present -- but never the full card number or CVV. The current `ILogger` calls serve debugging purposes; a dedicated `IAuditLogger` interface with a separate persistence backend (append-only store, write-once database table) would ensure audit records survive independently from application logs and cannot be tampered with.

### Observability [TODO -- **important for production readiness**]

A system handling money needs more than logs to understand its health. Three pillars are needed:

**Metrics** -- Key indicators: payment throughput (requests/sec), authorization rate vs decline rate, bank call latency (P50/P99), circuit-breaker state transitions, idempotent replay rate. These expose degrading trends (e.g., rising decline rate) before they become incidents. ASP.NET Core has built-in metrics; a Prometheus endpoint (`/metrics`) makes them scrapeable.

**Distributed tracing** -- A single payment request touches the controller, idempotency store, validator, bank HTTP call (with retries), and repository. Without tracing, a slow request only tells you "it was slow"; with OpenTelemetry, you see which step took the time and whether a retry fired. The correlation between the gateway's trace and the bank's trace is especially valuable for diagnosing bank-side latency.

**Structured logging** -- Current `ILogger` calls use string interpolation. Switching to structured log templates (e.g., `LogInformation("Payment {PaymentId} {Status} for card ending {LastFour}", ...)`) enables log aggregation systems to index and query by payment ID or status rather than full-text search.

## Code Structure / Extensibility

### Contracts Directory (`Services/Contracts/`)

Groups all service-level contracts -- interfaces and their associated data types (request/response DTOs) -- in one location. When a developer needs to understand how to interact with a service, they look in one place rather than scattering across `Interfaces/`, `Models/`, and `Enums/` directories.

**Contents:**
- `IBankService`, `IPaymentsRepository`, `IIdempotencyStore` -- service interfaces
- `BankRequest`, `BankResponse` -- bank service DTOs
- `ValidationResult`, `PaymentValidationError` -- validation contract types

### PaymentValidator: Concrete Class, Not Interface

`PaymentValidator` is a concrete class without an interface -- an intentional trade-off:

- **Why no interface now**: only one implementation exists; it's a pure function (stateless, no external dependencies), so testing doesn't require mocking it; controller tests can trigger validation by sending invalid data directly.
- **When to add `IPaymentValidator`**: if multiple banks with different validation rules are supported (e.g., Bank A requires 16-digit cards, Bank B accepts 14–19). At that point, bank-specific implementations (`VisaValidator`, `MastercardValidator`) would make sense. Adding an interface later is low-cost: extract the interface, register in DI, update the controller constructor.
- **Trade-off**: YAGNI vs. future extensibility. Current choice favors simplicity; the path to an interface is straightforward if multi-bank support is needed.

### BankService: Http vs Fake (Planned)

**Current wiring** -- `IBankService` is registered in `Program.cs` as the HTTP implementation:

```csharp
builder.Services.AddHttpClient<IBankService, BankService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankService:Url"] ?? "http://localhost:8080");
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddBankResiliencePipeline();
```

The `BankService` maps `PostPaymentRequest` → `BankRequest`, POSTs to `/payments`, and maps the bank JSON → `BankResponse`. On 503 it returns `Authorized: false` (triggering a Declined); on other non-success it returns `null` (also Declined).

**Planned: configuration switch** -- A `BankService:Provider` config key will switch between `Http` and `Fake`:

```json
{
  "BankService": {
    "Url": "http://localhost:8080",
    "Provider": "Http"   // or "Fake"
  }
}
```

A `FakeBankService` in `src/Services/Implementations/` will replicate the mountebank simulator logic (odd-ending card = authorized, even = declined, zero = unavailable), enabling development without Docker. The `FakeBankService` in `test/` (Unit folder) remains a controlled test stub where `Result` is set explicitly -- serving a different purpose.

### Test Organization

Tests are split into `Unit/` and `Functional/` directories with xUnit `[Trait]` markers:

- **Unit** -- no external dependencies, fast (`dotnet test --filter "Category=Unit"`)
- **Functional** -- full ASP.NET Core pipeline via `WebApplicationFactory<Program>` (`dotnet test --filter "Category=Functional"`)

Functional resilience tests (`BankResilienceTests`) use a `TrackingHandler` that programs a sequence of HTTP responses/errors, then verifies that the Polly pipeline retries correctly through the full API pipeline.
