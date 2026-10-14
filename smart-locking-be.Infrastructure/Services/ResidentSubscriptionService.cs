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
            ?? throw new NotFoundException("Không tìm thấy hồ sơ cư dân.");
        var plan = await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive, cancellationToken)
            ?? throw new NotFoundException("Gói cước không tồn tại hoặc đã ngừng đăng ký.");
        if (plan.Price <= 0 || plan.Price > int.MaxValue || decimal.Truncate(plan.Price) != plan.Price)
            throw new BadRequestException("Thanh toán PayOS yêu cầu số tiền VND nguyên dương.");
        if (!IsHttpUrl(options.Value.ReturnUrl) || !IsHttpUrl(options.Value.CancelUrl))
            throw new InternalServerException("Chưa cấu hình URL thanh toán PayOS hợp lệ.");

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

    public async Task ActivateSubscriptionAsync(PayOSWebhookData payment, CancellationToken cancellationToken = default)
    {
        if (payment.Code != "00" || payment.Currency != "VND" || payment.Amount <= 0 || string.IsNullOrWhiteSpace(payment.Reference) || payment.Reference.Length > 200)
            throw new BadRequestException("Thông tin thanh toán không hợp lệ.");
        string orderCode = payment.OrderCode.ToString(CultureInfo.InvariantCulture);
        Guid residentId = await db.ResidentSubscriptions.AsNoTracking().Where(s => s.ExternalOrderCode == orderCode)
            .Select(s => s.ResidentProfileId).SingleOrDefaultAsync(cancellationToken);
        if (residentId == Guid.Empty) throw new NotFoundException("Không tìm thấy đơn đăng ký.");

        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsRelational())
        {
            // Serialize all activations for this resident, including different order codes, across API instances.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"ResidentProfile\" WHERE \"Id\" = {residentId} FOR UPDATE", cancellationToken);
        }
        var subscription = await db.ResidentSubscriptions.SingleAsync(s => s.ExternalOrderCode == orderCode, cancellationToken);
        if (db.Database.IsRelational()) await db.Entry(subscription).ReloadAsync(cancellationToken);
        if (subscription.PriceSnapshot != payment.Amount) throw new BadRequestException("Số tiền thanh toán không khớp.");
        if (subscription.PaidAt.HasValue) return;
        if (subscription.Status != SubscriptionStatus.PendingPayment) throw new ConflictException("Đơn đăng ký không chờ thanh toán.");
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset? latestEnd = await db.ResidentSubscriptions.AsNoTracking()
            .Where(s => s.ResidentProfileId == residentId && s.Status == SubscriptionStatus.Active && s.EndDate > now)
            .MaxAsync(s => (DateTimeOffset?)s.EndDate, cancellationToken);
        TimeSpan duration = subscription.EndDate - subscription.StartDate;
        if (duration <= TimeSpan.Zero) throw new InternalServerException("Thời hạn đăng ký không hợp lệ.");
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
        if (db.Database.IsRelational())
            return await query.ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Status, SubscriptionStatus.Expired)
                .SetProperty(s => s.UpdatedAt, now), cancellationToken);
        var expired = await query.ToListAsync(cancellationToken);
        foreach (var subscription in expired) { subscription.Status = SubscriptionStatus.Expired; subscription.UpdatedAt = now; }
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static ResidentSubscriptionResponse Map(ResidentSubscription s) => new(s.Id, s.SubscriptionPlanId, s.SubscriptionPlan.Name,
        s.StartDate, s.EndDate, s.Status.ToString(), s.PriceSnapshot, s.ExternalOrderCode, s.PaidAt);
}
