using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Dashboards;

public sealed class AdminServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_WhenEmptyDatabase_ReturnsZeroMetricsAndEmptyLists()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        var result = await service.GetDashboardOverviewAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.Statistics);
        Assert.Equal(0, result.Statistics.TotalLockers);
        Assert.Equal(0, result.Statistics.ActiveLockers);
        Assert.Equal(0, result.Statistics.TotalCompartments);
        Assert.Equal(0, result.Statistics.TotalUsers);
        Assert.Equal(0, result.Statistics.StoredParcelsCount);
        Assert.Equal(0, result.Statistics.OverdueParcelsCount);
        Assert.Equal(0m, result.Statistics.TotalOverdueFeeAmountLast30Days);

        Assert.Equal(7, result.RecentParcelsTrend.Count);
        Assert.All(result.RecentParcelsTrend, day => Assert.Equal(0, day.Count));

        Assert.Empty(result.AttentionLockers);
        Assert.Empty(result.RecentLockers);
        Assert.Empty(result.RecentAuditLogs);
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_CalculatesUserMetricsCorrectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        dbContext.Users.AddRange(
            new User { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active },
            new User { Id = Guid.NewGuid(), Role = UserRole.Resident, Status = UserStatus.Active },
            new User { Id = Guid.NewGuid(), Role = UserRole.LockerOperator, Status = UserStatus.Active },
            new User { Id = Guid.NewGuid(), Role = UserRole.Administrator, Status = UserStatus.Active }
        );
        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        Assert.Equal(4, result.Statistics.TotalUsers);
        Assert.Equal(2, result.Statistics.ResidentCount);
        Assert.Equal(1, result.Statistics.OperatorCount);
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_CalculatesLockerAndCompartmentDistributionsCorrectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        var l1 = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-01",
            Address = "Sảnh A",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online
        };
        var l2 = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-02",
            Address = "Sảnh B",
            OperationalStatus = LockerOperationalStatus.OutOfService,
            ConnectionStatus = LockerConnectionStatus.Online
        };
        var l3 = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-03",
            Address = "Sảnh C",
            OperationalStatus = LockerOperationalStatus.Inactive,
            ConnectionStatus = LockerConnectionStatus.Offline
        };
        dbContext.Lockers.AddRange(l1, l2, l3);

        var cAvailable = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = l1.Id,
            Code = "A01",
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        var cOccupied = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = l1.Id,
            Code = "A02",
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        var cOverdue = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = l1.Id,
            Code = "A03",
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        var cMaintenance = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = l2.Id,
            Code = "B01",
            OperationalStatus = LockerCompartmentOperationalStatus.OutOfService
        };
        var cDisabled = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = l3.Id,
            Code = "C01",
            OperationalStatus = LockerCompartmentOperationalStatus.Inactive
        };
        dbContext.LockerCompartments.AddRange(cAvailable, cOccupied, cOverdue, cMaintenance, cDisabled);

        // Active parcels occupying compartments
        var d1 = new DeliveryRequest { Id = Guid.NewGuid(), AllocatedCompartmentId = cOccupied.Id, LockerId = l1.Id };
        var d2 = new DeliveryRequest { Id = Guid.NewGuid(), AllocatedCompartmentId = cOverdue.Id, LockerId = l1.Id };
        dbContext.DeliveryRequests.AddRange(d1, d2);

        var p1 = new Parcel { Id = Guid.NewGuid(), DeliveryRequestId = d1.Id, Status = ParcelStatus.Stored, DeliveryRequest = d1 };
        var p2 = new Parcel { Id = Guid.NewGuid(), DeliveryRequestId = d2.Id, Status = ParcelStatus.Overdue, DeliveryRequest = d2 };
        dbContext.Parcels.AddRange(p1, p2);

        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        // Lockers
        Assert.Equal(3, result.LockerStatus.Total);
        Assert.Equal(1, result.LockerStatus.Operational);
        Assert.Equal(1, result.LockerStatus.Maintenance);
        Assert.Equal(1, result.LockerStatus.Suspended);
        Assert.Equal(1, result.LockerStatus.Offline);

        // Compartments
        Assert.Equal(5, result.CompartmentStatus.Total);
        Assert.Equal(1, result.CompartmentStatus.Available);
        Assert.Equal(1, result.CompartmentStatus.Occupied);
        Assert.Equal(1, result.CompartmentStatus.Overdue);
        Assert.Equal(1, result.CompartmentStatus.Maintenance);
        Assert.Equal(1, result.CompartmentStatus.Disabled);

        Assert.Equal(1, result.Statistics.AvailableCompartments);
        Assert.Equal(2, result.Statistics.OccupiedCompartments); // 1 occupied + 1 overdue
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_CalculatesParcelMetricsAnd7DaysTrendCorrectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var d1 = new DeliveryRequest { Id = Guid.NewGuid() };
        var d2 = new DeliveryRequest { Id = Guid.NewGuid() };
        var d3 = new DeliveryRequest { Id = Guid.NewGuid() };
        var d4 = new DeliveryRequest { Id = Guid.NewGuid() };
        dbContext.DeliveryRequests.AddRange(d1, d2, d3, d4);

        var todayParcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = d1.Id,
            DeliveryRequest = d1,
            Status = ParcelStatus.Stored,
            StoredAt = now.AddHours(-1)
        };
        var overdueParcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = d2.Id,
            DeliveryRequest = d2,
            Status = ParcelStatus.Overdue,
            StoredAt = now.AddDays(-2)
        };
        var retrievedParcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = d3.Id,
            DeliveryRequest = d3,
            Status = ParcelStatus.Retrieved,
            StoredAt = now.AddDays(-4),
            RetrievedAt = now.AddDays(-3)
        };
        var oldParcel = new Parcel
        {
            Id = Guid.NewGuid(),
            DeliveryRequestId = d4.Id,
            DeliveryRequest = d4,
            Status = ParcelStatus.Retrieved,
            StoredAt = now.AddDays(-10)
        };

        dbContext.Parcels.AddRange(todayParcel, overdueParcel, retrievedParcel, oldParcel);
        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        Assert.Equal(2, result.Statistics.StoredParcelsCount); // Stored + Overdue
        Assert.Equal(1, result.Statistics.OverdueParcelsCount);
        Assert.Equal(1, result.Statistics.NewParcelsTodayCount);

        Assert.Equal(7, result.RecentParcelsTrend.Count);
        int totalTrendCount = result.RecentParcelsTrend.Sum(d => d.Count);
        Assert.Equal(3, totalTrendCount); // today, 2 days ago, 4 days ago (excludes 10 days ago)
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_CalculatesOverdueChargesSumLast30DaysCorrectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var c1 = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            Amount = 50000m,
            CreatedAt = now.AddDays(-5)
        };
        var c2 = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            Amount = 150000m,
            CreatedAt = now.AddDays(-20)
        };
        var cOld = new OverdueCharge
        {
            Id = Guid.NewGuid(),
            Amount = 300000m,
            CreatedAt = now.AddDays(-35)
        };

        dbContext.OverdueCharges.AddRange(c1, c2, cOld);
        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        Assert.Equal(200000m, result.Statistics.TotalOverdueFeeAmountLast30Days);
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_CalculatesIncidentCountsCorrectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        dbContext.Incidents.AddRange(
            new Incident { Id = Guid.NewGuid(), Status = IncidentStatus.Open, Title = "Kẹt cửa" },
            new Incident { Id = Guid.NewGuid(), Status = IncidentStatus.Investigating, Title = "Mất điện" },
            new Incident { Id = Guid.NewGuid(), Status = IncidentStatus.Escalated, Title = "Hỏng bo mạch chính" },
            new Incident { Id = Guid.NewGuid(), Status = IncidentStatus.Resolved, Title = "Đã thay ổ khóa" }
        );
        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        Assert.Equal(3, result.Statistics.OpenIncidentsCount); // Open, Investigating, Escalated
        Assert.Equal(1, result.Statistics.HighPriorityIncidentsCount); // Escalated
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_FiltersAttentionLockersCorrectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        var normal = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-OK",
            Address = "Bình thường",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online
        };
        var offline = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-OFFLINE",
            Address = "Mất mạng",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Offline
        };
        var maintenance = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-MAINT",
            Address = "Bảo trì",
            OperationalStatus = LockerOperationalStatus.OutOfService,
            ConnectionStatus = LockerConnectionStatus.Online
        };

        dbContext.Lockers.AddRange(normal, offline, maintenance);
        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        Assert.Equal(2, result.AttentionLockers.Count);
        Assert.Contains(result.AttentionLockers, l => l.Code == "LK-OFFLINE");
        Assert.Contains(result.AttentionLockers, l => l.Code == "LK-MAINT");
        Assert.DoesNotContain(result.AttentionLockers, l => l.Code == "LK-OK");
    }

    [Fact]
    public async Task GetDashboardOverviewAsync_ReturnsTop5RecentLockersAndAuditLogs()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        var operatorUser = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.LockerOperator,
            ResidentProfile = new ResidentProfile { Id = Guid.NewGuid(), FullName = "Nguyễn Văn Vận Hành" }
        };
        dbContext.Users.Add(operatorUser);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int i = 1; i <= 6; i++)
        {
            var locker = new Locker
            {
                Id = Guid.NewGuid(),
                Code = $"LK-0{i}",
                Address = $"Địa chỉ {i}",
                OperationalStatus = LockerOperationalStatus.Operational,
                ConnectionStatus = LockerConnectionStatus.Online,
                UpdatedAt = now.AddMinutes(i)
            };
            if (i == 6)
            {
                locker.OperatorAssignments.Add(new OperatorAssignment
                {
                    Id = Guid.NewGuid(),
                    OperatorUserId = operatorUser.Id,
                    OperatorUser = operatorUser,
                    AssignedAt = now
                });
            }
            dbContext.Lockers.Add(locker);
        }

        for (int i = 1; i <= 7; i++)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = $"Thao tác {i}",
                Result = AuditLogResult.Succeeded,
                OccurredAt = now.AddMinutes(i)
            });
        }

        await dbContext.SaveChangesAsync();

        var result = await service.GetDashboardOverviewAsync();

        Assert.Equal(5, result.RecentLockers.Count);
        Assert.Equal("LK-06", result.RecentLockers[0].Code);
        Assert.Equal("Nguyễn Văn Vận Hành", result.RecentLockers[0].OperatorName);

        Assert.Equal(5, result.RecentAuditLogs.Count);
        Assert.Equal("Thao tác 7", result.RecentAuditLogs[0].Action);
    }

    [Fact]
    public async Task GetSystemStatisticsAsync_ReturnsCorrectStatisticsDirectly()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var service = new AdminService(dbContext);

        dbContext.Users.AddRange(
            new User { Id = Guid.NewGuid(), Role = UserRole.Resident },
            new User { Id = Guid.NewGuid(), Role = UserRole.LockerOperator }
        );

        var locker = new Locker
        {
            Id = Guid.NewGuid(),
            Code = "LK-TEST",
            Address = "123 Test",
            OperationalStatus = LockerOperationalStatus.Operational,
            ConnectionStatus = LockerConnectionStatus.Online
        };
        dbContext.Lockers.Add(locker);

        var compartment = new LockerCompartment
        {
            Id = Guid.NewGuid(),
            LockerId = locker.Id,
            Code = "C01",
            OperationalStatus = LockerCompartmentOperationalStatus.Operational
        };
        dbContext.LockerCompartments.Add(compartment);

        dbContext.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(),
            Status = IncidentStatus.Open,
            Title = "Sự cố test"
        });

        await dbContext.SaveChangesAsync();

        var stats = await service.GetSystemStatisticsAsync();

        Assert.NotNull(stats);
        Assert.Equal(2, stats.TotalUsers);
        Assert.Equal(1, stats.ResidentCount);
        Assert.Equal(1, stats.OperatorCount);
        Assert.Equal(1, stats.TotalLockers);
        Assert.Equal(1, stats.ActiveLockers);
        Assert.Equal(1, stats.TotalCompartments);
        Assert.Equal(1, stats.AvailableCompartments);
        Assert.Equal(0, stats.OccupiedCompartments);
        Assert.Equal(1, stats.OpenIncidentsCount);
    }
}
