using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;

namespace smart_locking_be.API.Controllers;

[ApiController, Route("api/webhooks/payos"), AllowAnonymous]
public sealed class PaymentWebhooksController(IPaymentGatewayService gateway, IResidentSubscriptionService subscriptions) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PayOS(PayOSWebhookPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var payment = gateway.VerifyWebhook(payload);
            await subscriptions.ActivateSubscriptionAsync(payment, cancellationToken);
            return Ok();
        }
        // PayOS sends a signed sample order when registering the webhook. Other orders
        // on this channel may also be unrelated to subscriptions; never create one here.
        catch (NotFoundException) { return Ok(); }
    }
}
