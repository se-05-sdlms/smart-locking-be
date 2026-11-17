using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Residents;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Residents;

public sealed class ResidentAccountChangeServiceTests
{
    [Theory]
    [InlineData(false, "Rejected", "Rejected")]
    [InlineData(true, "Submitted", "ApprovalBlocked")]
    public async Task ReviewExistingRequest_PersistsNewHistoryAndPreservesIdentity(
        bool approve, string expectedStatus, string expectedAction)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var admin = new User { Id = Guid.NewGuid(), Role = UserRole.Administrator, Status = UserStatus.Active };
        var resident = new User { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active, PhoneNumber = "0901234567" };
        var item = new ResidentAccountChange
        {
            Id = Guid.NewGuid(),
            ResidentUserId = resident.Id,
            Kind = "Phone",
            Reason = "Change phone",
            OriginalPhoneNumber = resident.PhoneNumber,
            NewPhoneNumber = "0907654321",
            PhoneVerificationExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        item.Events.Add(new ResidentAccountChangeEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = resident.Id,
            Action = "Submitted",
            Message = "Submitted",
            CreatedAt = item.CreatedAt
        });
        db.AddRange(admin, resident, new ResidentProfile { Id = Guid.NewGuid(), UserId = resident.Id }, item);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new ResidentAccountChangeService(db, null!, null!, TimeProvider.System);
        var result = await service.ReviewAsync(admin.Id, item.Id, new ReviewAccountChangeRequest(approve, "Review reason"), default);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(2, result.History.Count);
        Assert.Single(result.History.Where(e => e.Action == expectedAction));
        if (approve) Assert.Contains(result.Blockers, b => b.Kind == "PhoneVerification");
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Set<ResidentAccountChangeEvent>().CountAsync());
        Assert.Equal("0901234567", (await db.Users.SingleAsync(u => u.Id == resident.Id)).PhoneNumber);
        Assert.Equal(expectedStatus, (await db.Set<ResidentAccountChange>().SingleAsync()).Status);
        Assert.Equal(approve ? 0 : 1, await db.Notifications.CountAsync());
    }
}
