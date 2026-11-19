using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Options;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Policies;

public sealed class IncidentSlaServiceUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("locker", IncidentPriority.Critical)]
    [InlineData("compartment", IncidentPriority.High)]
    [InlineData("payment", IncidentPriority.High)]
    [InlineData("retrieval", IncidentPriority.High)]
    [InlineData("other", IncidentPriority.Low)]
    [InlineData("unknown", IncidentPriority.Medium)]
    [Trait("Category", "INCIDENT-SLA")]
    public void Priority_MapsIncidentType(string type, IncidentPriority expected)
    {
        Assert.Equal(expected, IncidentSlaService.GetPriority(type));
    }

    [Fact]
    [Trait("Category", "INCIDENT-SLA")]
    public void Targets_UseDefaultsWithoutActivePolicy()
    {
        var defaults = new IncidentSlaOptions();

        SlaTargets result = IncidentSlaService.TargetsFor(IncidentPriority.Critical, null, defaults);

        Assert.Equal(defaults.Critical.AcknowledgeMinutes, result.AcknowledgeMinutes);
        Assert.Equal(defaults.Critical.ResolveMinutes, result.ResolveMinutes);
    }

    [Fact]
    [Trait("Category", "INCIDENT-SLA")]
    public void Targets_UsePolicyValuesWhenPolicyExists()
    {
        var defaults = new IncidentSlaOptions();
        var policy = new SystemPolicy
        {
            IncidentCriticalAcknowledgeMinutes = 7,
            IncidentCriticalResolveMinutes = 17,
            IncidentHighAcknowledgeMinutes = 8,
            IncidentHighResolveMinutes = 18,
            IncidentMediumAcknowledgeMinutes = 9,
            IncidentMediumResolveMinutes = 19,
            IncidentLowAcknowledgeMinutes = 10,
            IncidentLowResolveMinutes = 20
        };

        SlaTargets result = IncidentSlaService.TargetsFor(IncidentPriority.Low, policy, defaults);

        Assert.Equal(10, result.AcknowledgeMinutes);
        Assert.Equal(20, result.ResolveMinutes);
    }

    [Fact]
    [Trait("Category", "INCIDENT-SLA")]
    public async Task Run_WhenDisabled_DoesNotChangeIncidents()
    {
        await using var db = CreateDbContext();
        var incident = Incident(createdAt: Now.AddHours(-2));
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var options = new IncidentSlaOptions { Enabled = false };
        var service = new IncidentSlaService(db, Options.Create(options), new FixedTimeProvider(Now));

        IncidentSlaRunResult result = await service.RunAsync();

        Assert.Equal(new IncidentSlaRunResult(0, 0), result);
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    [Trait("Category", "INCIDENT-SLA")]
    public async Task Run_AtWarningThreshold_CreatesOneNotification()
    {
        await using var db = CreateDbContext();
        var admin = User(UserRole.Administrator);
        var incident = Incident(createdAt: Now.AddMinutes(-12), type: "locker");
        db.Users.Add(admin);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var options = new IncidentSlaOptions
        {
            WarningPercent = 80,
            Critical = new SlaTargets { AcknowledgeMinutes = 15, ResolveMinutes = 60 }
        };
        var service = new IncidentSlaService(db, Options.Create(options), new FixedTimeProvider(Now));

        IncidentSlaRunResult first = await service.RunAsync();
        IncidentSlaRunResult second = await service.RunAsync();

        Assert.Equal(1, first.Warned);
        Assert.Equal(0, first.Escalated);
        Assert.Equal(new IncidentSlaRunResult(0, 0), second);
        Assert.Single(db.Notifications.Where(item => item.Type == "IncidentSlaAcknowledgeWarning"));
    }

    [Fact]
    [Trait("Category", "INCIDENT-SLA")]
    public async Task Run_AfterAcknowledgeDeadline_EscalatesIncidentAndCreatesAudit()
    {
        await using var db = CreateDbContext();
        var admin = User(UserRole.Administrator);
        var incident = Incident(createdAt: Now.AddMinutes(-16), type: "locker");
        db.Users.Add(admin);
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();

        var service = new IncidentSlaService(
            db,
            Options.Create(new IncidentSlaOptions { Critical = new SlaTargets { AcknowledgeMinutes = 15, ResolveMinutes = 60 } }),
            new FixedTimeProvider(Now));

        IncidentSlaRunResult result = await service.RunAsync();

        Assert.Equal(new IncidentSlaRunResult(0, 1), result);
        Assert.Equal(IncidentStatus.Escalated, incident.Status);
        Assert.Equal(Now, incident.EscalatedAt);
        Assert.Contains(db.IncidentActions, item => item.IncidentId == incident.Id && item.ToStatus == IncidentStatus.Escalated);
        Assert.Contains(db.AuditLogs, item => item.Action == "Incident.SlaEscalated");
        Assert.Single(db.Notifications.Where(item => item.UserId == admin.Id && item.Type == "IncidentEscalated"));
    }

    private static ApplicationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static User User(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}",
        PasswordHash = "hash",
        Role = role,
        Status = UserStatus.Active,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static Incident Incident(DateTimeOffset createdAt, string type = "other") => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Source = IncidentSource.System,
        Status = IncidentStatus.Open,
        Title = "Locker issue",
        Description = "Test incident",
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
