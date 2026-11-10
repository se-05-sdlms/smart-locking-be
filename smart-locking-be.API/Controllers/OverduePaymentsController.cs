using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/overdue-payments"), Authorize(Roles = "Resident")]
public sealed class OverduePaymentsController(IOverduePaymentService payments) : ControllerBase
{
    [HttpPost("charges/{chargeId:guid}/checkout")]
    [EnableRateLimiting(RateLimitPolicyNames.HeavyAction)]
    public async Task<IActionResult> CreateCheckout(Guid chargeId, CancellationToken cancellationToken) =>
        Ok(await payments.CreateCheckoutAsync(User.GetRequiredUserId(), chargeId, cancellationToken));

    [HttpGet("{transactionId:guid}")]
    public async Task<IActionResult> Get(Guid transactionId, CancellationToken cancellationToken) =>
        Ok(await payments.GetAsync(User.GetRequiredUserId(), transactionId, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> GetHistory(CancellationToken cancellationToken) =>
        Ok(await payments.GetHistoryAsync(User.GetRequiredUserId(), cancellationToken));
}
