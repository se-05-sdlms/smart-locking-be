using Microsoft.EntityFrameworkCore;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Auth;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Persistence.Seeding;

namespace smart_locking_be.Tests.Persistence;

public sealed class DemoDataSeederTests
{
    [Fact]
    public async Task SeedAsync_CoversEveryEntityAndKeepsExternalCredentialsInactive()
    {
        const string password = "Test-only-demo-password-123!";
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await DemoDataSeeder.SeedAsync(db, password);

        var entries = db.ChangeTracker.Entries().ToArray();
        Assert.Equal(41, db.Model.GetEntityTypes().Count());
        Assert.Equal(618, entries.Length);
        Assert.All(db.Model.GetEntityTypes(), type =>
            Assert.True(entries.Count(entry => entry.Metadata == type) >= 10, type.Name));
        await db.SaveChangesAsync();

        var admin = await db.Users.SingleAsync(user => user.Id == DemoDataSeeder.AdminId);
        Assert.Equal(UserRole.Administrator, admin.Role);
        Assert.True(new Pbkdf2PasswordHashService().VerifyPassword(password, admin.PasswordHash));
        Assert.Equal(1, await db.SystemPolicies.CountAsync(policy => policy.IsActive));
        Assert.All(await db.Lockers.ToListAsync(), locker => Assert.Equal(LockerConnectionStatus.Offline, locker.ConnectionStatus));
        Assert.All(await db.Set<DeviceInstallation>().ToListAsync(), device => Assert.False(device.IsActive));
        Assert.All(await db.RefreshTokens.ToListAsync(), token => Assert.NotNull(token.RevokedAt));
        Assert.All(await db.ResidentBiometrics.ToListAsync(), biometric => Assert.NotNull(biometric.RevokedAt));
    }
}
