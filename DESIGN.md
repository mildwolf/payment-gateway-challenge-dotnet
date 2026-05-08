# Payment Gateway - Design Considerations & Assumptions

## API Design

### POST /api/Payments
Processes a payment request. Validation failures return HTTP 200 with `Status: Rejected` rather than HTTP 400, keeping the response format uniform — the client always receives a `PostPaymentResponse` regardless of outcome.

### GET /api/Payments/{id}
Retrieves a previously processed payment. Returns 404 if not found. Returns a `GetPaymentResponse` which is a separate model from `PostPaymentResponse` to allow independent evolution (e.g., GET may later include authorization codes or additional metadata).

## Key Design Decisions

### Validation failures return 200 + Rejected, not 400
The gateway's job is to process a payment request and report the result. A validation failure is one possible result, not a protocol error. This simplifies client error handling — there's one response shape and one success status code to parse.

### Rejected payments are not stored
A `Rejected` payment is a validation failure (bad input), not a payment event. The response returns `Id: Guid.Empty` and the client should fix the input and retry. This keeps the repository clean and avoids ambiguity on GET.

### Bank 503 → Declined
When the bank simulator returns 503 Service Unavailable, the payment is marked `Declined`. This makes the gateway resilient — it stays available and responsive even when the bank is down. Network errors (unreachable bank) also map to `Declined` for the same reason.

### CardNumber in request, last four in response
The request accepts the full card number as a string (needed to send to the bank). The response only includes the last four digits. The full card number is never stored — only `CardNumberLastFour` is persisted in the repository.

### CardNumberLastFour and Cvv as strings
Changed from `int` to `string` to preserve leading zeros. A card ending in "0456" stored as int would become 456. A CVV of "034" stored as int would become 34.

### Separate PostPaymentResponse and GetPaymentResponse
These models currently have the same fields but serve different operations. Keeping them separate allows them to evolve independently — for example, GET might later include an authorization code or timestamp that POST doesn't need.

### ConcurrentDictionary for thread safety
The in-memory repository uses `ConcurrentDictionary` instead of `List` to handle concurrent requests safely without explicit locking.

### IHttpClientFactory for bank communication
`BankService` receives `HttpClient` via DI using `AddHttpClient`, which manages handler lifetimes and avoids socket exhaustion from creating/disposing `HttpClient` instances.

### No external validation framework
Validation rules are straightforward (6 fields, simple checks). A plain class with regex and range checks is sufficient — adding FluentValidation or data annotations would be over-engineering for this scope.

### Idempotency-Key mechanism
Clients can prevent duplicate charges by including an `Idempotency-Key` header in POST requests. When the gateway receives a request with a key it has already processed, it returns the original result without calling the bank again.

**Behavior rules:**
- The key is optional — requests without a key are processed normally
- The same key is processed only once; subsequent requests return the original result (same ID, same status)
- Rejected requests (validation failures) do not store the key — this allows clients to fix their input and retry with the same key
- Only authorized/declined payments get their key stored

**In-memory limitations:**
- Keys are lost on process restart (no persistence)
- Keys are not shared across nodes in a multi-node cluster — node A's key is invisible to node B

**Production-grade solution (Redis + DB dual-layer):**
1. Request arrives → check Redis (fast path, ~1ms)
2. Redis miss → check DB (slow path, unique constraint as safety net)
3. DB miss → process normally, insert into DB (unique constraint prevents concurrent duplicates), write back to Redis
4. If Redis is down, the DB unique constraint is the final safety net — no duplicate charges even in degraded mode

### Hand-written fakes over mocking libraries
Test doubles are simple fakes (`FakeBankService`) rather than dynamic mocks (Moq). For this scale of project, a hand-written fake is more readable and has zero dependencies.

### Expiry date interpretation
A card expiring in month M/year Y is valid through the last day of that month. Validation checks `new DateTime(Y, M, 1).AddMonths(1) > DateTime.UtcNow`. The `ExpiryYear` is 4-digit (e.g., 2026) in the API, converted to 2-digit (MM/YY) only when mapping to the bank's format.

## Assumptions

1. **In-memory storage is sufficient** — No persistent database is required for this challenge. The repository is a singleton `ConcurrentDictionary`.
2. **No authentication/authorization** — The challenge does not require API keys or user authentication.
3. **Idempotency is optional and in-memory** — The `Idempotency-Key` header is optional. Keys are stored in-memory (`InMemoryIdempotencyStore`), which means they are lost on restart and not shared across nodes. A Redis + DB dual-layer implementation would be needed for production.
4. **No retry on bank failure** — If the bank is unavailable (503 or network error), the payment is immediately declined. No retry logic is implemented.
5. **Single currency per payment** — Each payment has one currency code (ISO 4217, 3 uppercase letters).
6. **Amount in minor units** — The amount field uses the smallest currency unit (e.g., 100 = 1.00 GBP). No decimal handling needed.
7. **Bank simulator is the source of truth** — The gateway trusts the bank's authorization decision. No fraud checking is performed by the gateway itself.

## Bank Simulator Integration

The bank simulator (mountebank) runs on `http://localhost:8080` and accepts POST `/payments` with:
```json
{ "card_number": "...", "expiry_date": "MM/YY", "currency": "GBP", "amount": 1000, "cvv": "123" }
```

Response rules:
- Card ending with odd digit → 200 `{ "authorized": true, "authorization_code": "<guid>" }`
- Card ending with even digit → 200 `{ "authorized": false, "authorization_code": "" }`
- Card ending with 0 → 503 (empty body)
- Missing fields → 400 with error message

The bank URL is configurable via `appsettings.json` (`BankService:Url`).
