# PaymentGateway.Api.Tests

## How to Run

```bash
# Run all tests
dotnet test

# Run only Unit tests
dotnet test --filter "Category=Unit"

# Run only Functional tests
dotnet test --filter "Category=Functional"

# Run only End2End tests (requires mountebank on http://localhost:8080)
dotnet test --filter "Category=End2End"

# Run all tests except End2End (no bank simulator needed)
dotnet test --filter "Category!=End2End"
```

## Test Categories

### Unit (31 tests)

No external dependencies. All HTTP calls are mocked or bypassed.

#### BankServiceTests (4 tests)

Tests `BankService` with a mock `HttpMessageHandler` to simulate bank responses.

| Test | Scenario |
|------|----------|
| `ProcessPaymentAsync_Authorized_ReturnsAuthorizedWithCode` | Bank returns 200 with `authorized: true` and an authorization code |
| `ProcessPaymentAsync_Declined_ReturnsDeclinedWithEmptyCode` | Bank returns 200 with `authorized: false` and empty authorization code |
| `ProcessPaymentAsync_ServiceUnavailable_ReturnsDeclined` | Bank returns 503 Service Unavailable |
| `ProcessPaymentAsync_NetworkError_ReturnsNull` | Bank is completely unreachable (network error) |

#### PaymentValidatorTests (21 tests)

Tests `PaymentValidator` with pure input validation logic.

| Test | Scenario |
|------|----------|
| `Validate_ValidRequest_IsValid` | All fields valid |
| `Validate_CardNumber14Digits_IsValid` | Card number = 14 digits (min valid) |
| `Validate_CardNumber19Digits_IsValid` | Card number = 19 digits (max valid) |
| `Validate_CardNumberTooShort_HasCardNumberInvalid` | Card number < 14 digits |
| `Validate_CardNumberTooLong_HasCardNumberInvalid` | Card number > 19 digits |
| `Validate_CardNumberWithLetters_HasCardNumberInvalid` | Non-numeric card number |
| `Validate_CardNumberEmpty_HasCardNumberInvalid` | Empty card number |
| `Validate_ExpiryMonthZero_HasExpiryMonthInvalid` | Month = 0 |
| `Validate_ExpiryMonthThirteen_HasExpiryMonthInvalid` | Month = 13 |
| `Validate_ExpiredCard_HasCardExpired` | Card expiry date is in the past |
| `Validate_CardExpiresThisMonth_IsValid` | Card expires this month (still valid) |
| `Validate_CurrencyLowercase_HasCurrencyInvalid` | Lowercase currency code |
| `Validate_CurrencyTwoChars_HasCurrencyInvalid` | 2-char currency code |
| `Validate_CurrencyNumeric_HasCurrencyInvalid` | Numeric currency code |
| `Validate_NegativeAmount_HasAmountInvalid` | Negative amount |
| `Validate_ZeroAmount_IsValid` | Zero amount (card check) |
| `Validate_CvvThreeDigits_IsValid` | CVV = 3 digits (min valid) |
| `Validate_CvvFourDigits_IsValid` | CVV = 4 digits (max valid, Amex) |
| `Validate_CvvTwoDigits_HasCvvInvalid` | CVV < 3 digits |
| `Validate_CvvFiveDigits_HasCvvInvalid` | CVV > 4 digits |
| `Validate_CvvWithLetters_HasCvvInvalid` | Non-numeric CVV |
| `Validate_MultipleErrors_ReturnsAllErrorFlags` | Multiple invalid fields return all error flags |

#### InMemoryIdempotencyStoreTests (4 tests)

Tests `InMemoryIdempotencyStore` with pure in-memory logic.

| Test | Scenario |
|------|----------|
| `TryAdd_ThenTryGet_ReturnsPayment` | Store and retrieve by key |
| `TryGet_UnknownKey_ReturnsNull` | Key not found |
| `TryAdd_DuplicateKey_DoesNotOverwrite` | First write wins on duplicate key |
| `ConcurrentTryAdd_OnlyFirstWins` | Thread-safety under concurrent writes |

### Functional (11 tests)

Uses `WebApplicationFactory<Program>` to run the full ASP.NET Core pipeline. Bank calls are stubbed with `FakeBankService`.

#### PaymentsControllerTests (11 tests)

| Test | Scenario |
|------|----------|
| `RetrievesAPaymentSuccessfully` | GET existing payment returns 200 with correct data |
| `Returns404IfPaymentNotFound` | GET non-existent payment returns 404 |
| `PostPayment_ReturnsRejected_WhenValidationFails` | POST with invalid fields returns Rejected status |
| `PostPayment_ReturnsAuthorized_WhenBankAuthorizes` | POST with bank approval returns Authorized status |
| `PostPayment_ReturnsDeclined_WhenBankDeclines` | POST with bank decline returns Declined status |
| `PostPayment_ReturnsDeclined_WhenBankIsUnavailable` | POST when bank is unreachable returns Declined status |
| `PostThenGet_ReturnsSamePayment` | POST then GET returns consistent data |
| `PostWithIdempotencyKey_DuplicateRequest_ReturnsSamePayment` | Same idempotency key returns same payment |
| `PostWithDifferentIdempotencyKeys_CreatesDifferentPayments` | Different keys create different payments |
| `PostWithoutIdempotencyKey_CreatesNewPaymentEachTime` | No idempotency key creates new payment each time |
| `PostRejectedWithKey_ThenValidWithSameKey_CreatesPayment` | Rejected request does not store idempotency key, allowing retry |

### End2End (7 tests)

Uses `WebApplicationFactory<Program>` with **real** `BankService` calling the mountebank simulator on `http://localhost:8080`. Tests are automatically skipped if the simulator is not running.

Prerequisite: `docker-compose up` to start the bank simulator.

#### BankSimulatorTests (7 tests)

| Test | Scenario |
|------|----------|
| `PostPayment_CardEndsOdd1_ReturnsAuthorized` | Card ending 1 (odd) → Authorized with authorization code |
| `PostPayment_CardEndsOdd3_ReturnsAuthorized` | Card ending 3 (odd) → Authorized with authorization code |
| `PostPayment_CardEndsEven2_ReturnsDeclined` | Card ending 2 (even) → Declined, empty authorization code |
| `PostPayment_CardEndsEven4_ReturnsDeclined` | Card ending 4 (even) → Declined, empty authorization code |
| `PostPayment_CardEndsZero_ReturnsDeclined` | Card ending 0 → bank 503, gateway Declined |
| `PostThenGet_CardEndsOdd_DataIntact` | Authorized payment → GET returns consistent data |
| `PostThenGet_CardEndsEven_DataIntact` | Declined payment → GET returns consistent data |
