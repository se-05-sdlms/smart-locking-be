using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using smart_locking_be.API.Authorization;
using smart_locking_be.API.Constants;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/payment-reconciliations"), Authorize(Roles = "Resident,Administrator")]
public sealed class PaymentReconciliationsController(IPaymentReconciliationService service) : ControllerBase
{
    [HttpGet("payments")]
    public async Task<IActionResult> Payments([FromQuery] PaymentLookupQuery query, CancellationToken cancellationToken) =>
        Ok(await service.GetPaymentsAsync(User.GetRequiredUserId(), query, cancellationToken));
    [HttpGet("payments/{kind}/{paymentId:guid}")]
    public async Task<IActionResult> Payment(string kind, Guid paymentId, CancellationToken cancellationToken) =>
        Ok(await service.GetPaymentAsync(User.GetRequiredUserId(), kind, paymentId, cancellationToken));
    [HttpGet]
    public async Task<IActionResult> Cases(CancellationToken cancellationToken, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 30) =>
        Ok(await service.GetCasesAsync(User.GetRequiredUserId(), pageNumber, pageSize, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.GetCaseAsync(User.GetRequiredUserId(), id, cancellationToken));
    [HttpPost, EnableRateLimiting(RateLimitPolicyNames.HeavyAction)]
    public async Task<IActionResult> Create(CreatePaymentReconciliationRequest request, CancellationToken cancellationToken) =>
        Ok(await service.CreateAsync(User.GetRequiredUserId(), request, cancellationToken));
    [HttpPost("{id:guid}:assign"), Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> Assign(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.AssignAsync(User.GetRequiredUserId(), id, cancellationToken));
    [HttpPost("{id:guid}:respond"), Authorize(Policy = ApiPolicies.Administrator)]
    public async Task<IActionResult> Respond(Guid id, RespondPaymentReconciliationRequest request, CancellationToken cancellationToken) =>
        Ok(await service.RespondAsync(User.GetRequiredUserId(), id, request, cancellationToken));
    [HttpPost("{id:guid}:synchronize"), Authorize(Policy = ApiPolicies.Administrator), EnableRateLimiting(RateLimitPolicyNames.HeavyAction)]
    public async Task<IActionResult> Synchronize(Guid id, CancellationToken cancellationToken) =>
        Ok(await service.SynchronizeAsync(User.GetRequiredUserId(), id, cancellationToken));
}
