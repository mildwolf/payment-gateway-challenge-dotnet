# Payment Gateway Design Decisions

## Contracts Directory (`Services/Contracts/`)

The `Contracts` directory groups all service-level contracts: interfaces and their associated data types (request/response DTOs). This provides a single location to understand the full protocol for each service boundary.

**Contents:**
- `IBankService`, `IPaymentsRepository`, `IIdempotencyStore` — service interfaces
- `BankRequest`, `BankResponse` — bank service DTOs
- `ValidationResult`, `PaymentValidationError` — validation contract types

**Rationale:** When a developer needs to understand how to interact with a service, they look in one place rather than scattering across `Interfaces/`, `Models/`, and `Enums/` directories.

## PaymentValidator: Concrete Class, Not Interface

`PaymentValidator` is currently a concrete class without an interface. This is an intentional trade-off:

**Why no interface now:**
- Only one implementation exists; no need to swap at runtime
- It is a pure function (stateless, no external dependencies), so testing doesn't require mocking it
- Controller tests can trigger validation by sending invalid data directly

**When to add `IPaymentValidator`:**
- If multiple banks with different validation rules are supported (e.g., Bank A requires 16-digit cards, Bank B accepts 14-19)
- At that point, a `IPaymentValidator` with bank-specific implementations (`VisaValidator`, `MastercardValidator`) would make sense
- Adding an interface later is low-cost: extract the interface, register in DI, update the controller constructor

**Trade-off:** YAGNI vs. future extensibility. Current choice favors simplicity; the path to an interface is straightforward if multi-bank support is needed.

## BankService: Http vs Fake (Planned)

### Current Wiring

The `IBankService` is registered in `Program.cs` (line 12) as the HTTP implementation:

```csharp
// src/PaymentGateway.Api/Program.cs:12
builder.Services.AddHttpClient<IBankService, BankService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BankService:Url"] ?? "http://localhost:8080");
});
```

The bank URL comes from `appsettings.json` (line 9-11):

```json
// src/PaymentGateway.Api/appsettings.json
{
  "BankService": {
    "Url": "http://localhost:8080"
  }
}
```

The controller consumes `IBankService` via constructor injection (`Controllers/PaymentsController.cs:23`).

### Key Files

| File | Role |
|------|------|
| `Services/Contracts/IBankService.cs` | Interface: `Task<BankResponse?> ProcessPaymentAsync(PostPaymentRequest)` |
| `Services/Contracts/BankRequest.cs` | DTO sent to bank (card_number, expiry_date, currency, amount, cvv) |
| `Services/Contracts/BankResponse.cs` | DTO returned from bank (authorized, authorization_code) |
| `Services/Implementations/BankService.cs` | HTTP implementation: POST to `/payments`, maps `PostPaymentRequest` → `BankRequest`, maps bank JSON → `BankResponse` |
| `test/.../Unit/FakeBankService.cs` | Test stub: returns preset `Result` for controlled testing |
| `Controllers/PaymentsController.cs` | Consumer: injects `IBankService`, calls `ProcessPaymentAsync`, maps result to `PostPaymentResponse` |

### Planned: Configuration Switch

A `BankService:Provider` config key will switch between `Http` and `Fake` in `Program.cs`:

```json
{
  "BankService": {
    "Url": "http://localhost:8080",
    "Provider": "Http"   // or "Fake"
  }
}
```

The `FakeBankService` to be added in `src/Services/Implementations/` will replicate the mountebank simulator logic (odd-ending card = authorized, even = declined, zero = unavailable), enabling development without docker.

The `FakeBankService` in `test/` (Unit folder) remains a controlled test stub where `Result` is set explicitly, serving a different purpose than the src-internal simulator.

## Test Organization

Tests are split into `Unit/` and `Functional/` directories with xUnit `[Trait]` markers:

- **Unit** — no external dependencies, fast (`dotnet test --filter "Category=Unit"`)
- **Functional** — full ASP.NET Core pipeline via `WebApplicationFactory<Program>` (`dotnet test --filter "Category=Functional"`)

`ValidationResult` and `PaymentValidationError` use a `[Flags]` enum pattern so tests can assert exact error codes rather than just checking for non-empty error lists.
