using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Payments;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Payments;

public sealed class PaymentReconciliationServiceTests
{
    [Theory]
    [InlineData("Assign", "InReview")]
    [InlineData("Respond", "InReview")]
    [InlineData("Respond", "WaitingProvider")]
    [InlineData("Respond", "Closed")]
    public async Task ExistingCase_SavesHistoryAndDoesNotConfirmPayment(string operation, string status)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var admin = new User { Id = Guid.NewGuid(), Role = UserRole.Administrator, Status = UserStatus.Active };
        var payment = new PaymentTransaction { Id = Guid.NewGuid(), Status = PaymentTransactionStatus.Pending };
        var item = new PaymentReconciliation
        {
            Id = Guid.NewGuid(),
            Reference = "REC-TEST",
            ResidentUserId = Guid.NewGuid(),
            PaymentTransactionId = payment.Id,
            Reason = "Bank debit reported",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        item.Events.Add(new PaymentReconciliationEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = admin.Id,
            Action = "Submitted",
            Status = "Submitted",
            Message = "Submitted",
            CreatedAt = item.CreatedAt
        });
        db.AddRange(admin, payment, item);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new PaymentReconciliationService(db, null!, null!, null!, TimeProvider.System);

        var result = operation == "Assign"
            ? await service.AssignAsync(admin.Id, item.Id)
            : await service.RespondAsync(admin.Id, item.Id, new(status, "Review response"));

        Assert.Equal(status, result.Status);
        Assert.Equal(admin.Id, result.AssignedAdminUserId);
        Assert.Equal(2, result.History.Count);
        Assert.Single(result.History.Where(e => e.Action == (operation == "Assign" ? "Assigned" : "Responded")));
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Set<PaymentReconciliationEvent>().CountAsync());
        Assert.Equal(status, (await db.Set<PaymentReconciliation>().SingleAsync()).Status);
        Assert.Equal(PaymentTransactionStatus.Pending, (await db.PaymentTransactions.SingleAsync()).Status);
        Assert.Equal(operation == "Assign" ? 0 : 1, await db.Notifications.CountAsync());
    }
}
