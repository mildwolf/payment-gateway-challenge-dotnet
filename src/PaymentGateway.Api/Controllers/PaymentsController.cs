using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IBankService _bankService;
    private readonly PaymentValidator _paymentValidator;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentsRepository paymentsRepository,
        IBankService bankService,
        ILogger<PaymentsController> logger)
    {
        _paymentsRepository = paymentsRepository;
        _bankService = bankService;
        _paymentValidator = new PaymentValidator();
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<PostPaymentResponse>> PostPaymentAsync([FromBody] PostPaymentRequest request)
    {
        _logger.LogInformation("Payment request received for card ending {LastFour}",
            request.CardNumber.Length >= 4 ? request.CardNumber[^4..] : "****");

        var validationErrors = _paymentValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning("Payment rejected due to validation errors: {Errors}", string.Join(", ", validationErrors));
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

        var authorized = await _bankService.ProcessPaymentAsync(request);

        var status = authorized is true
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
            Amount = request.Amount
        };

        _paymentsRepository.Add(payment);

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
