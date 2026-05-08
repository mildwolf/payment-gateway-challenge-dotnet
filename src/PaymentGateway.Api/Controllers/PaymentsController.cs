using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Services.Contracts;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IBankService _bankService;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly PaymentValidator _paymentValidator;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentsRepository paymentsRepository,
        IBankService bankService,
        IIdempotencyStore idempotencyStore,
        ILogger<PaymentsController> logger)
    {
        _paymentsRepository = paymentsRepository;
        _bankService = bankService;
        _idempotencyStore = idempotencyStore;
        _paymentValidator = new PaymentValidator();
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync([FromBody] PostPaymentRequest request)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (idempotencyKey is not null)
        {
            var existingPayment = _idempotencyStore.TryGet(idempotencyKey);
            if (existingPayment is not null)
            {
                _logger.LogInformation("Idempotent request for key {Key}, returning existing payment {Id}", idempotencyKey, existingPayment.Id);
                return Ok(existingPayment);
            }
        }

        _logger.LogInformation("Payment request received for card ending {LastFour}",
            request.CardNumber.Length >= 4 ? request.CardNumber[^4..] : "****");

        var validationResult = _paymentValidator.Validate(request);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Payment rejected due to validation errors: {Errors}", string.Join(", ", validationResult.Messages));
            return Ok(new PostPaymentResponse
            {
                Id = Guid.Empty,
                Status = PaymentStatus.Rejected,
                CardNumberLastFour = string.Empty,
                ExpiryMonth = request.ExpiryMonth,
                ExpiryYear = request.ExpiryYear,
                Currency = request.Currency,
                Amount = request.Amount
            });
        }

        var bankResponse = await _bankService.ProcessPaymentAsync(request);

        var status = bankResponse?.Authorized is true
            ? PaymentStatus.Authorized
            : PaymentStatus.Declined;

        _logger.LogInformation("Bank response: {Status}", status);

        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            Status = status,
            CardNumberLastFour = request.CardNumber[^4..],
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            Currency = request.Currency,
            Amount = request.Amount,
            AuthorizationCode = bankResponse?.AuthorizationCode ?? string.Empty
        };

        _paymentsRepository.Add(payment);

        if (idempotencyKey is not null)
            _idempotencyStore.TryAdd(idempotencyKey, payment);

        return Ok(payment);
    }

    [HttpGet("{id:guid}")]
    public ActionResult<GetPaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        if (payment is null)
            return NotFound();

        return Ok(payment);
    }
}
