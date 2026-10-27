using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using System.Globalization;
using System.Security.Cryptography;

namespace smart_locking_be.Infrastructure.Services;

public sealed class ResidentSubscriptionService(ApplicationDbContext db, IPaymentGatewayService gateway,
    IOptions<PayOSOptions> options, TimeProvider clock) : IResidentSubscriptionService
{
    public async Task<SubscriptionCheckoutResponse> SubscribeAsync(Guid userId, SubscribePlanRequest request, CancellationToken cancellationToken = default)
    {
        var resident = await db.ResidentProfiles.AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrorCodes.NotFound, "Resident profile not found.");
        var plan = await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive, cancellationToken)
            ?? throw new NotFoundException(SubscriptionErrorCodes.NotFound, "Subscription plan is unavailable.");
        if (plan.Price == 0) return await ActivateFreePlanAsync(resident.Id, plan, cancellationToken);
        if (plan.Price <= 0 || plan.Price > int.MaxValue || decimal.Truncate(plan.Price) != plan.Price)
            throw new BadRequestException(SubscriptionErrorCodes.InvalidPayment, "PayOS payment requires a positive VND amount.");
        if (!IsHttpUrl(options.Value.ReturnUrl) || !IsHttpUrl(options.Value.CancelUrl))
            throw new InternalServerException("A valid PayOS payment URL is not configured.");

        DateTimeOffset now = clock.GetUtcNow();
        // Random positive 52-bit code stays exact in JavaScript and works across API instances.
        long orderCode = (BitConverter.ToInt64(RandomNumberGenerator.GetBytes(8)) & 0x000FFFFFFFFFFFFF) + 1;
        var subscription = new ResidentSubscription
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = resident.Id,
            SubscriptionPlanId = plan.Id,
            Status = SubscriptionStatus.PendingPayment,
            PriceSnapshot = plan.Price,
            ExternalOrderCode = orderCode.ToString(CultureInfo.InvariantCulture),
            // Preserve the purchased duration in the pending interval, independent of later plan edits.
            StartDate = now,
            EndDate = now.AddDays(plan.DurationDays),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ResidentSubscriptions.Add(subscription);
        // Persist first so fast webhooks or an ambiguous provider timeout remain recoverable.
        await db.SaveChangesAsync(cancellationToken);
        var link = await gateway.CreatePaymentLinkAsync(new(orderCode, checked((int)plan.Price), "Subscription",
            options.Value.ReturnUrl, options.Value.CancelUrl), cancellationToken);
        return new(subscription.Id, link.CheckoutUrl, orderCode, plan.Price);
    }

    // A free plan needs no PayOS checkout: it takes effect immediately and queues after any current period.
    private async Task<SubscriptionCheckoutResponse> ActivateFreePlanAsync(Guid residentId, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational())
        {
            // Same lock as paid activation so concurrent subscribes for one resident cannot stack twice.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"ResidentProfile\" WHERE \"Id\" = {residentId} FOR UPDATE", cancellationToken);
        }

        DateTimeOffset now = clock.GetUtcNow();
        var active = await db.ResidentSubscriptions.AsNoTracking()
            .Where(s => s.ResidentProfileId == residentId && s.Status == SubscriptionStatus.Active && s.EndDate > now)
            .Select(s => new { s.SubscriptionPlanId, s.EndDate }).ToListAsync(cancellationToken);
        // Free periods must not be farmed: one running or queued period per free plan.
        if (active.Any(s => s.SubscriptionPlanId == plan.Id))
            throw new ConflictException(SubscriptionErrorCodes.AlreadyExists, "This free plan is already active.");

        DateTimeOffset start = active.Count > 0 ? active.Max(s => s.EndDate) : now;
        var subscription = new ResidentSubscription
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = residentId,
            SubscriptionPlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            PriceSnapshot = 0,
            StartDate = start,
            EndDate = start.AddDays(plan.DurationDays),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ResidentSubscriptions.Add(subscription);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(subscription.Id, string.Empty, 0, 0, Activated: true);
    }

    public async Task ActivateSubscriptionAsync(PayOSWebhookData payment, CancellationToken cancellationToken = default)
    {
        if (payment.Code != "00" || payment.Currency != "VND" || payment.Amount <= 0 || string.IsNullOrWhiteSpace(payment.Reference) || payment.Reference.Length > 200)
            throw new BadRequestException(SubscriptionErrorCodes.InvalidPayment, "Payment information is invalid.");
        string orderCode = payment.OrderCode.ToString(CultureInfo.InvariantCulture);
        Guid residentId = await db.ResidentSubscriptions.AsNoTracking().Where(s => s.ExternalOrderCode == orderCode)
            .Select(s => s.ResidentProfileId).SingleOrDefaultAsync(cancellationToken);
        if (residentId == Guid.Empty) throw new NotFoundException(SubscriptionErrorCodes.NotFound, "Subscription not found.");

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational())
        {
            // Serialize all activations for this resident, including different order codes, across API instances.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"ResidentProfile\" WHERE \"Id\" = {residentId} FOR UPDATE", cancellationToken);
        }
        var subscription = await db.ResidentSubscriptions.SingleAsync(s => s.ExternalOrderCode == orderCode, cancellationToken);
        if (db.Database.IsRelational()) await db.Entry(subscription).ReloadAsync(cancellationToken);
        if (subscription.PriceSnapshot != payment.Amount) throw new BadRequestException(SubscriptionErrorCodes.InvalidPayment, "Payment amount does not match.");
        if (subscription.PaidAt.HasValue) return;
        if (subscription.Status != SubscriptionStatus.PendingPayment) throw new ConflictException(SubscriptionErrorCodes.InvalidState, "Subscription is not awaiting payment.");
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset? latestEnd = await db.ResidentSubscriptions.AsNoTracking()
            .Where(s => s.ResidentProfileId == residentId && s.Status == SubscriptionStatus.Active && s.EndDate > now)
            .MaxAsync(s => (DateTimeOffset?)s.EndDate, cancellationToken);
        TimeSpan duration = subscription.EndDate - subscription.StartDate;
        if (duration <= TimeSpan.Zero) throw new InternalServerException("Subscription duration is invalid.");
        subscription.StartDate = latestEnd ?? now;
        subscription.EndDate = subscription.StartDate.Add(duration);
        subscription.Status = SubscriptionStatus.Active;
        subscription.PaidAt = now;
        subscription.ExternalTransactionId = payment.Reference;
        subscription.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ResidentSubscriptionResponse?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var row = await db.ResidentSubscriptions.AsNoTracking().Include(s => s.SubscriptionPlan)
            .Where(s => s.ResidentProfile.UserId == userId && s.Status == SubscriptionStatus.Active && s.StartDate <= now && s.EndDate > now)
            .OrderBy(s => s.StartDate).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ResidentSubscriptionResponse>> GetHistoryAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await db.ResidentSubscriptions.AsNoTracking().Include(s => s.SubscriptionPlan)
            .Where(s => s.ResidentProfile.UserId == userId).OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<int> ExpireOutdatedSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = clock.GetUtcNow();
        var query = db.ResidentSubscriptions.Where(s => s.Status == SubscriptionStatus.Active && s.EndDate < now);
        // ponytail: tracked expiry keeps each status change auditable; revisit bulk updates if volume grows.
        var expired = await query.ToListAsync(cancellationToken);
        foreach (var subscription in expired) { subscription.Status = SubscriptionStatus.Expired; subscription.UpdatedAt = now; }
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static ResidentSubscriptionResponse Map(ResidentSubscription s) => new(s.Id, s.SubscriptionPlanId, s.SubscriptionPlan.Name,
        s.StartDate, s.EndDate, s.Status.ToString(), s.PriceSnapshot, s.ExternalOrderCode, s.PaidAt);
}
