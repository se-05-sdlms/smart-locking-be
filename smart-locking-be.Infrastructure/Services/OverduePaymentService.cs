using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using System.Globalization;
using System.Security.Cryptography;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OverduePaymentService(ApplicationDbContext db, IPaymentGatewayService gateway,
    IOptions<PayOSOptions> options, TimeProvider clock) : IOverduePaymentService
{
    public async Task<OverduePaymentResponse> CreateCheckoutAsync(Guid residentUserId, Guid chargeId,
        CancellationToken cancellationToken = default)
    {
        if (!IsHttpUrl(options.Value.OverdueReturnUrl) || !IsHttpUrl(options.Value.OverdueCancelUrl))
            throw new InternalServerException("Overdue payment return and cancel URLs are not configured.");

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (transaction is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"OverdueCharge\" WHERE \"Id\" = {chargeId} FOR UPDATE", cancellationToken);

        OverdueCharge charge = await db.OverdueCharges
            .Include(item => item.Parcel).ThenInclude(item => item.DeliveryRequest).ThenInclude(item => item.ResidentProfile)
            .SingleOrDefaultAsync(item => item.Id == chargeId &&
                item.Parcel.DeliveryRequest.ResidentProfile!.UserId == residentUserId, cancellationToken)
            ?? throw new NotFoundException(PaymentErrorCodes.NotFound, "Overdue charge not found.");
        if (charge.Status != OverdueChargeStatus.Outstanding)
            throw new ConflictException(PaymentErrorCodes.InvalidRequest, "Overdue charge is already paid.");
        if (charge.Currency != "VND" || charge.Amount <= 0 || charge.Amount > int.MaxValue ||
            decimal.Truncate(charge.Amount) != charge.Amount)
            throw new BadRequestException(PaymentErrorCodes.InvalidRequest, "PayOS requires a positive whole VND amount.");

        PaymentTransaction? payment = await db.PaymentTransactions
            .SingleOrDefaultAsync(item => item.OverdueChargeId == chargeId &&
                item.Status == PaymentTransactionStatus.Pending, cancellationToken);
        if (payment is null)
        {
            long orderCode = (BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & 0x000FFFFFFFFFFFFF) + 1;
            payment = new PaymentTransaction
            {
                Id = Guid.NewGuid(),
                OverdueChargeId = charge.Id,
                ExternalOrderCode = orderCode.ToString(CultureInfo.InvariantCulture),
                Amount = charge.Amount,
                Currency = charge.Currency,
                Status = PaymentTransactionStatus.Pending,
                RequestedAt = clock.GetUtcNow()
            };
            db.PaymentTransactions.Add(payment);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (payment.CheckoutUrl is not null) return Map(payment);

        // Persist the order before calling PayOS so a fast webhook or provider timeout remains reconcilable.
        long code = long.Parse(payment.ExternalOrderCode!, CultureInfo.InvariantCulture);
        CreatePaymentLinkResult link = await gateway.CreatePaymentLinkAsync(new(code, checked((int)payment.Amount),
            "Overdue storage fee", options.Value.OverdueReturnUrl, options.Value.OverdueCancelUrl), cancellationToken);
        payment.CheckoutUrl = link.CheckoutUrl;
        await db.SaveChangesAsync(cancellationToken);
        return Map(payment);
    }

    public async Task<OverduePaymentResponse> GetAsync(Guid residentUserId, Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        PaymentTransaction payment = await db.PaymentTransactions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == transactionId &&
                item.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId == residentUserId, cancellationToken)
            ?? throw new NotFoundException(PaymentErrorCodes.NotFound, "Payment transaction not found.");
        return Map(payment);
    }

    public async Task<IReadOnlyList<OverduePaymentResponse>> GetHistoryAsync(Guid residentUserId,
        CancellationToken cancellationToken = default) =>
        (await db.PaymentTransactions.AsNoTracking()
            .Where(item => item.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId == residentUserId)
            .OrderByDescending(item => item.RequestedAt).ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<bool> TryCompleteAsync(PayOSWebhookData webhook, CancellationToken cancellationToken = default)
    {
        string orderCode = webhook.OrderCode.ToString(CultureInfo.InvariantCulture);
        Guid chargeId = await db.PaymentTransactions.AsNoTracking()
            .Where(item => item.ExternalOrderCode == orderCode)
            .Select(item => item.OverdueChargeId).SingleOrDefaultAsync(cancellationToken);
        if (chargeId == Guid.Empty) return false;

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (transaction is not null)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"OverdueCharge\" WHERE \"Id\" = {chargeId} FOR UPDATE", cancellationToken);
        PaymentTransaction payment = await db.PaymentTransactions
            .Include(item => item.OverdueCharge).ThenInclude(item => item.Parcel)
                .ThenInclude(item => item.DeliveryRequest).ThenInclude(item => item.ResidentProfile)
            .SingleAsync(item => item.ExternalOrderCode == orderCode, cancellationToken);
        if (payment.Amount != webhook.Amount || payment.Currency != webhook.Currency ||
            string.IsNullOrWhiteSpace(webhook.Reference) || webhook.Reference.Length > 150)
            throw new BadRequestException(PaymentErrorCodes.InvalidWebhook, "Payment amount or reference is invalid.");
        if (payment.Status != PaymentTransactionStatus.Pending) return true;

        DateTimeOffset now = clock.GetUtcNow();
        bool succeeded = webhook.Code == "00";
        payment.Status = succeeded ? PaymentTransactionStatus.Success : PaymentTransactionStatus.Failed;
        payment.CompletedAt = now;
        payment.ExternalTransactionId = webhook.Reference;
        if (succeeded)
        {
            payment.OverdueCharge.Status = OverdueChargeStatus.Paid;
            payment.OverdueCharge.PaidAt = now;
            payment.OverdueCharge.UpdatedAt = now;
        }
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = payment.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId,
            Action = succeeded ? "OverduePayment.Succeeded" : "OverduePayment.Failed",
            EntityType = nameof(PaymentTransaction),
            EntityId = payment.Id,
            Result = succeeded ? AuditLogResult.Succeeded : AuditLogResult.Failed,
            Details = $"Overdue charge {chargeId}, order {orderCode}",
            OccurredAt = now
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = payment.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile.UserId,
            PaymentTransactionId = payment.Id,
            ParcelId = payment.OverdueCharge.ParcelId,
            Type = succeeded ? "OverduePaymentSucceeded" : "OverduePaymentFailed",
            Channel = NotificationChannel.InApp,
            Title = succeeded ? "Overdue fee paid" : "Overdue fee payment failed",
            Message = succeeded ? "Your overdue storage fee has been paid. You can collect your parcel."
                : "Your overdue storage fee payment failed. Please try again.",
            DeliveryStatus = NotificationDeliveryStatus.Sent,
            SentAt = now,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https";
    private static OverduePaymentResponse Map(PaymentTransaction payment) => new(payment.Id,
        payment.OverdueChargeId, payment.Amount, payment.Currency, payment.Status,
        payment.CheckoutUrl, payment.RequestedAt, payment.CompletedAt);
}
