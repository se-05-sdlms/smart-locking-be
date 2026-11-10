using Microsoft.EntityFrameworkCore;
using Npgsql;
using smart_locking_be.Application.DTOs.Subscriptions;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class SubscriptionPlanService(ApplicationDbContext db, TimeProvider clock) : ISubscriptionPlanService
{
    public async Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(bool activeOnly = true, CancellationToken cancellationToken = default) =>
        (await db.SubscriptionPlans.AsNoTracking().Where(p => !activeOnly || p.IsActive)
            .OrderBy(p => p.Code).ToListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<SubscriptionPlanResponse> CreateAsync(CreateSubscriptionPlanRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request.Name, request.Price, request.DurationDays, request.MaxActiveParcels);
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 50)
            throw BadRequestException.For("code", "Subscription plan code must contain between 1 and 50 characters.");
        string code = request.Code.Trim();
        if (await db.SubscriptionPlans.AnyAsync(p => p.Code == code, cancellationToken))
            throw new ConflictException(SubscriptionErrorCodes.AlreadyExists, "Subscription plan code already exists.");
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            Price = request.Price,
            DurationDays = request.DurationDays,
            MaxActiveParcels = request.MaxActiveParcels,
            IsActive = true,
            CreatedAt = clock.GetUtcNow(),
            UpdatedAt = clock.GetUtcNow()
        };
        db.SubscriptionPlans.Add(plan);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ConflictException(SubscriptionErrorCodes.AlreadyExists, "Subscription plan code already exists."); }
        return Map(plan);
    }

    public async Task<SubscriptionPlanResponse> UpdateAsync(Guid id, UpdateSubscriptionPlanRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request.Name, request.Price, request.DurationDays, request.MaxActiveParcels);
        var plan = await db.SubscriptionPlans.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException(SubscriptionErrorCodes.NotFound, "Subscription plan not found.");
        plan.Name = request.Name.Trim();
        plan.Description = request.Description;
        plan.Price = request.Price;
        plan.DurationDays = request.DurationDays;
        plan.MaxActiveParcels = request.MaxActiveParcels;
        plan.IsActive = request.IsActive;
        plan.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Map(plan);
    }

    private static void Validate(string name, decimal price, int days, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || price < 0 || price > int.MaxValue ||
            decimal.Truncate(price) != price || days <= 0 || days > 36500 || capacity <= 0)
            throw BadRequestException.For("plan", "Subscription plan data is invalid.");
    }

    private static SubscriptionPlanResponse Map(SubscriptionPlan p) => new(p.Id, p.Code, p.Name, p.Description, p.Price, p.DurationDays, p.MaxActiveParcels, p.IsActive);
}
