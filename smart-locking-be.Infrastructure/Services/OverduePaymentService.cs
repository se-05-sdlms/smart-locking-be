using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace smart_locking_be.Infrastructure.Services;

public sealed class OverduePaymentService(ApplicationDbContext db, IPaymentGatewayService gateway,
    IOptions<PayOSOptions> options, TimeProvider clock, ILogger<OverduePaymentService> logger) : IOverduePaymentService
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
        if (await db.PaymentTransactions.AnyAsync(item => item.OverdueChargeId == chargeId &&
            item.Status == PaymentTransactionStatus.Success, cancellationToken))
            throw new ConflictException(PaymentErrorCodes.InvalidRequest,
                "A received payment requires reconciliation. Do not create another payment order.");
        if (charge.Currency != "VND" || charge.Amount <= 0 || charge.Amount > int.MaxValue ||
            decimal.Truncate(charge.Amount) != charge.Amount)
            throw new BadRequestException(PaymentErrorCodes.InvalidRequest, "PayOS requires a positive whole VND amount.");

        PaymentTransaction? payment = await db.PaymentTransactions
            .SingleOrDefaultAsync(item => item.OverdueChargeId == chargeId &&
                item.Status == PaymentTransactionStatus.Pending, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        if (payment is not null && (payment.CheckoutUrl is not null || payment.RequestedAt.AddMinutes(15) <= now))
        {
            long existingCode = long.Parse(payment.ExternalOrderCode!, CultureInfo.InvariantCulture);
            string? status;
            try
            {
                status = await gateway.GetPaymentLinkStatusAsync(existingCode, cancellationToken);
            }
            catch (ServiceUnavailableException) when (payment.CheckoutUrl is null)
            {
                // The original create call may have failed before PayOS received the order.
                status = null;
            }
            if (status == "PENDING" && payment.RequestedAt.AddMinutes(15) <= now)
                status = await gateway.CancelPaymentLinkAsync(existingCode, cancellationToken);

            if (status is "CANCELLED" or "EXPIRED")
            {
                payment.Status = PaymentTransactionStatus.Cancelled;
                payment.CompletedAt = now;
                payment.FailureReason = "Payment link cancelled or expired.";
                DateTimeOffset cutoff = now > charge.Parcel.MaxStorageUntil ? charge.Parcel.MaxStorageUntil : now;
                int billableHours = (int)Math.Ceiling((cutoff - charge.Parcel.PickupDueAt).TotalHours);
                charge.Amount = billableHours * charge.RatePerHourSnapshot;
                charge.CalculatedThrough = cutoff;
                charge.UpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                if (charge.Amount <= 0 || charge.Amount > int.MaxValue || decimal.Truncate(charge.Amount) != charge.Amount)
                {
                    if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                    throw new BadRequestException(PaymentErrorCodes.InvalidRequest, "PayOS requires a positive whole VND amount.");
                }
                payment = null;
            }
            else if (status == "PENDING")
            {
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return Map(payment);
            }
            else if (status is not null)
            {
                throw new ConflictException(PaymentErrorCodes.InvalidRequest,
                    "Payment is being processed; wait for the verified webhook before trying again.");
            }
        }
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
                RequestedAt = now
            };
            db.PaymentTransactions.Add(payment);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (payment.CheckoutUrl is not null) return Map(payment);

        // Persist the order before calling PayOS so a fast webhook or provider timeout remains reconcilable.
        long code = long.Parse(payment.ExternalOrderCode!, CultureInfo.InvariantCulture);
        CreatePaymentLinkResult link = await gateway.CreatePaymentLinkAsync(new(code, checked((int)payment.Amount),
            "Overdue storage fee", options.Value.OverdueReturnUrl, options.Value.OverdueCancelUrl,
            clock.GetUtcNow().AddMinutes(15)), cancellationToken);
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
        if (db.Database.IsRelational())
        {
            await db.Entry(payment).ReloadAsync(cancellationToken);
            await db.Entry(payment.OverdueCharge).ReloadAsync(cancellationToken);
        }
        if (payment.Amount != webhook.Amount || payment.Currency != webhook.Currency ||
            string.IsNullOrWhiteSpace(webhook.Reference) || webhook.Reference.Length > 150)
        {
            logger.LogWarning("Ignoring PayOS webhook with mismatched payment data for order {OrderCode}.", orderCode);
            return true;
        }
        bool succeeded = webhook.Code == "00";
        if (payment.Status == PaymentTransactionStatus.Success ||
            (!succeeded && payment.Status != PaymentTransactionStatus.Pending)) return true;

        DateTimeOffset now = clock.GetUtcNow();
        bool settlementConflict = succeeded && (payment.OverdueCharge.Status != OverdueChargeStatus.Outstanding ||
            payment.Amount != payment.OverdueCharge.Amount || payment.Currency != payment.OverdueCharge.Currency ||
            await db.PaymentTransactions.AnyAsync(other => other.OverdueChargeId == chargeId && other.Id != payment.Id &&
                (other.Status == PaymentTransactionStatus.Pending || other.Status == PaymentTransactionStatus.Success), cancellationToken));
        // A verified debit is a financial fact even when it cannot safely settle the shared
        // charge (late cancelled order, replacement order, amount change or second debit).
        payment.Status = succeeded ? PaymentTransactionStatus.Success : PaymentTransactionStatus.Failed;
        payment.CompletedAt = now;
        payment.ExternalTransactionId = webhook.Reference;
        payment.FailureReason = settlementConflict ? "Verified payment conflicts with the current charge or another order. Reconciliation required." : null;
        if (succeeded && !settlementConflict)
        {
            payment.OverdueCharge.Status = OverdueChargeStatus.Paid;
            payment.OverdueCharge.PaidAt = now;
            payment.OverdueCharge.UpdatedAt = now;
        }
        if (settlementConflict) await RecordConflictAsync(payment, webhook, now, cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = payment.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId,
            Action = settlementConflict ? "OverduePayment.SettlementConflict" : succeeded ? "OverduePayment.Succeeded" : "OverduePayment.Failed",
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
            Type = settlementConflict ? "OverduePaymentConflict" : succeeded ? "OverduePaymentSucceeded" : "OverduePaymentFailed",
            Channel = NotificationChannel.InApp,
            Title = settlementConflict ? "Overdue payment needs review" : succeeded ? "Overdue fee paid" : "Overdue fee payment failed",
            Message = settlementConflict ? "Your payment was received but conflicts with another order or the current charge. Do not pay again; contact support for reconciliation."
                : succeeded ? "Your overdue storage fee has been paid. You can collect your parcel."
                : "Your overdue storage fee payment failed. Please try again.",
            DeliveryStatus = NotificationDeliveryStatus.Sent,
            SentAt = now,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task RecordConflictAsync(PaymentTransaction payment, PayOSWebhookData webhook, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // All financial paths serialize on the charge, including reconciliation case creation.
        PaymentReconciliation? item = await db.Set<PaymentReconciliation>().Include(row => row.Events)
            .SingleOrDefaultAsync(row => row.PaymentTransactionId == payment.Id, cancellationToken);
        if (item is not null && db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PaymentReconciliation\" WHERE \"Id\" = {item.Id} FOR UPDATE", cancellationToken);
            await db.Entry(item).ReloadAsync(cancellationToken);
        }
        if (item is null)
        {
            Guid id = Guid.NewGuid();
            item = new PaymentReconciliation
            {
                Id = id,
                Reference = $"REC-{id:N}",
                PaymentTransactionId = payment.Id,
                ResidentUserId = payment.OverdueCharge.Parcel.DeliveryRequest.ResidentProfile!.UserId,
                Reason = "A verified payment could not safely settle the overdue charge.",
                CreatedAt = now
            };
            db.Set<PaymentReconciliation>().Add(item);
        }
        item.Status = "Conflict";
        item.Response = "Payment received. The current charge or another payment order conflicts; keep this reference and do not pay again while an administrator investigates.";
        item.ProviderStatus = "PAID";
        item.ProviderCheckedAt = now;
        item.UpdatedAt = now;
        item.Events.Add(new PaymentReconciliationEvent
        {
            Id = Guid.NewGuid(),
            PaymentReconciliationId = item.Id,
            ActorUserId = item.ResidentUserId,
            Action = "PaymentConflict",
            Status = item.Status,
            Message = item.Response,
            ProviderEvidence = JsonSerializer.Serialize(webhook),
            CreatedAt = now
        });
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https";
    private static OverduePaymentResponse Map(PaymentTransaction payment) => new(payment.Id,
        payment.OverdueChargeId, payment.Amount, payment.Currency, payment.Status,
        payment.CheckoutUrl, payment.RequestedAt, payment.CompletedAt);
}
