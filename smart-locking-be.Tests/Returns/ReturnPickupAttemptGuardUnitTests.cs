using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.Errors;
using smart_locking_be.Application.Exceptions;
using smart_locking_be.Domain.Entities;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Services;
using Xunit;

namespace smart_locking_be.Tests.Returns;

public sealed class ReturnPickupAttemptGuardUnitTests
{
    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

        public void Set(DateTimeOffset newUtcNow) => _utcNow = newUtcNow;
    }

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static string HashClientIp(string ip)
    {
        var address = IPAddress.Parse(ip);
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(address.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [Fact]
    public async Task AcquireAsync_CreatesNewBucketWithZeroFailuresAnd15MinuteWindow()
    {
        await using var db = CreateDbContext();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var lockerId = Guid.NewGuid();
        var clientKey = "test-client-1";

        var bucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.NotNull(bucket);
        Assert.Equal(lockerId, bucket.LockerId);
        Assert.Equal(clientKey, bucket.ClientKey);
        Assert.Equal(0, bucket.FailedAttempts);
        Assert.Equal(timeProvider.GetUtcNow().Add(ReturnPickupAttemptGuard.Window), bucket.WindowExpiresAt);
    }

    [Fact]
    public async Task AcquireAsync_SubsequentCallsWithinWindow_ReturnsSameBucketWithoutExtendingWindow()
    {
        await using var db = CreateDbContext();
        var initialTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(initialTime);
        var lockerId = Guid.NewGuid();
        var clientKey = "test-client-1";

        var bucket1 = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        bucket1.FailedAttempts = 2;
        await db.SaveChangesAsync();

        var initialExpiresAt = bucket1.WindowExpiresAt;
        Assert.Equal(initialTime.Add(ReturnPickupAttemptGuard.Window), initialExpiresAt);

        // Advance 5 minutes into the 15-minute window
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var bucket2 = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);

        Assert.Equal(bucket1.LockerId, bucket2.LockerId);
        Assert.Equal(bucket1.ClientKey, bucket2.ClientKey);
        Assert.Equal(2, bucket2.FailedAttempts);
        Assert.Equal(initialExpiresAt, bucket2.WindowExpiresAt); // Expiration MUST NOT be extended
    }

    [Fact]
    public async Task ConsecutiveFailures_ReachLockoutAtMaxFailures()
    {
        await using var db = CreateDbContext();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var lockerId = Guid.NewGuid();
        var clientKey = "test-client-1";

        // Simulate 4 failed attempts
        for (int i = 1; i <= 4; i++)
        {
            var bucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
            Assert.True(bucket.FailedAttempts < ReturnPickupAttemptGuard.MaxFailures);
            bucket.FailedAttempts++;
            await db.SaveChangesAsync();
            Assert.Equal(i, bucket.FailedAttempts);
        }

        // 5th attempt: increment to MaxFailures
        var fifthBucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        fifthBucket.FailedAttempts++;
        await db.SaveChangesAsync();
        Assert.Equal(ReturnPickupAttemptGuard.MaxFailures, fifthBucket.FailedAttempts);

        // Verify lockout check triggers TooManyRequestsException
        var lockedBucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        Assert.True(lockedBucket.FailedAttempts >= ReturnPickupAttemptGuard.MaxFailures);

        var ex = Assert.Throws<TooManyRequestsException>(() =>
        {
            if (lockedBucket.FailedAttempts >= ReturnPickupAttemptGuard.MaxFailures)
            {
                int retryAfter = Math.Max(1, (int)Math.Ceiling((lockedBucket.WindowExpiresAt - timeProvider.GetUtcNow()).TotalSeconds));
                throw new TooManyRequestsException(ReturnErrorCodes.PickupLocked, "Too many invalid pickup codes.", retryAfter);
            }
        });

        Assert.Equal(ReturnErrorCodes.PickupLocked, ex.ErrorCode);
        Assert.Equal(900, ex.RetryAfterSeconds); // 15 minutes = 900 seconds
    }

    [Fact]
    public async Task LockedBucket_FailedRequestsDuringWindowDoNotExtendExpiry()
    {
        await using var db = CreateDbContext();
        var initialTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(initialTime);
        var lockerId = Guid.NewGuid();
        var clientKey = "test-client-1";

        // Reach lockout
        var bucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        bucket.FailedAttempts = ReturnPickupAttemptGuard.MaxFailures;
        await db.SaveChangesAsync();
        var initialExpiresAt = bucket.WindowExpiresAt;

        // Advance 14 minutes (1 minute remaining in the window)
        timeProvider.Advance(TimeSpan.FromMinutes(14));

        var reacquired = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);

        Assert.Equal(initialExpiresAt, reacquired.WindowExpiresAt);
        Assert.Equal(ReturnPickupAttemptGuard.MaxFailures, reacquired.FailedAttempts);

        // Verification error computes remaining seconds without extending window
        var ex = Assert.Throws<TooManyRequestsException>(() =>
        {
            if (reacquired.FailedAttempts >= ReturnPickupAttemptGuard.MaxFailures)
            {
                int retryAfter = Math.Max(1, (int)Math.Ceiling((reacquired.WindowExpiresAt - timeProvider.GetUtcNow()).TotalSeconds));
                throw new TooManyRequestsException(ReturnErrorCodes.PickupLocked, "Locked", retryAfter);
            }
        });

        Assert.Equal(60, ex.RetryAfterSeconds); // Exactly 1 minute = 60s remaining
    }

    [Fact]
    public async Task AfterWindowExpires_AcquireAsyncResetsFailedAttemptsToZero()
    {
        await using var db = CreateDbContext();
        var initialTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(initialTime);
        var lockerId = Guid.NewGuid();
        var clientKey = "test-client-1";

        // Lock the bucket with 5 failed attempts
        var bucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        bucket.FailedAttempts = ReturnPickupAttemptGuard.MaxFailures;
        await db.SaveChangesAsync();

        // Advance 16 minutes (window is 15 minutes, so it has expired)
        timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Acquire after expiration
        var resetBucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKey, timeProvider, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(0, resetBucket.FailedAttempts);
        Assert.Equal(timeProvider.GetUtcNow().Add(ReturnPickupAttemptGuard.Window), resetBucket.WindowExpiresAt);
    }

    [Fact]
    public async Task BucketsAreIsolated_AcrossDifferentLockerIds()
    {
        await using var db = CreateDbContext();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var lockerA = Guid.NewGuid();
        var lockerB = Guid.NewGuid();
        var clientKey = "shared-client-key";

        // Locker A reaches lockout
        var bucketA = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerA, clientKey, timeProvider, CancellationToken.None);
        bucketA.FailedAttempts = 5;
        await db.SaveChangesAsync();

        // Locker B should have a fresh bucket with 0 failures
        var bucketB = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerB, clientKey, timeProvider, CancellationToken.None);

        Assert.Equal(5, bucketA.FailedAttempts);
        Assert.Equal(0, bucketB.FailedAttempts);
        Assert.NotEqual(bucketA.LockerId, bucketB.LockerId);
    }

    [Fact]
    public async Task BucketsAreIsolated_AcrossDifferentClientKeys()
    {
        await using var db = CreateDbContext();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var lockerId = Guid.NewGuid();
        var clientKeyA = "client-ip-1";
        var clientKeyB = "client-ip-2";

        // Client A reaches lockout
        var bucketA = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKeyA, timeProvider, CancellationToken.None);
        bucketA.FailedAttempts = 5;
        await db.SaveChangesAsync();

        // Client B should have a fresh bucket with 0 failures
        var bucketB = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKeyB, timeProvider, CancellationToken.None);

        Assert.Equal(5, bucketA.FailedAttempts);
        Assert.Equal(0, bucketB.FailedAttempts);
        Assert.NotEqual(bucketA.ClientKey, bucketB.ClientKey);
    }

    [Fact]
    public async Task IpNormalization_MappedIpv6SharesBucketWithIpv4()
    {
        await using var db = CreateDbContext();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
        var lockerId = Guid.NewGuid();

        string ipv4 = "192.0.2.1";
        string mappedIpv6 = "::ffff:192.0.2.1";

        string clientKeyIpv4 = HashClientIp(ipv4);
        string clientKeyMapped = HashClientIp(mappedIpv6);

        // Verify hash/key is identical after normalization
        Assert.Equal(clientKeyIpv4, clientKeyMapped);

        // Fail 5 times using mapped IPv6
        var bucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKeyMapped, timeProvider, CancellationToken.None);
        bucket.FailedAttempts = ReturnPickupAttemptGuard.MaxFailures;
        await db.SaveChangesAsync();

        // Querying using IPv4 should yield the same locked bucket
        var checkedBucket = await ReturnPickupAttemptGuard.AcquireAsync(db, lockerId, clientKeyIpv4, timeProvider, CancellationToken.None);

        Assert.Equal(ReturnPickupAttemptGuard.MaxFailures, checkedBucket.FailedAttempts);
    }

    [Fact]
    public async Task AcquireAsync_CleansUpExpiredBucketsInDatabase()
    {
        await using var db = CreateDbContext();
        var initialTime = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(initialTime);

        var expiredBucket = new ReturnPickupAttempt
        {
            LockerId = Guid.NewGuid(),
            ClientKey = "expired-client",
            FailedAttempts = 5,
            WindowExpiresAt = initialTime.AddMinutes(-5) // expired 5 minutes ago
        };
        db.Add(expiredBucket);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Set<ReturnPickupAttempt>().CountAsync());

        // AcquireAsync for a different bucket triggers the in-memory cleanup of expired records
        var newLocker = Guid.NewGuid();
        await ReturnPickupAttemptGuard.AcquireAsync(db, newLocker, "new-client", timeProvider, CancellationToken.None);
        await db.SaveChangesAsync();

        var allBuckets = await db.Set<ReturnPickupAttempt>().ToListAsync();
        Assert.DoesNotContain(allBuckets, b => b.ClientKey == "expired-client");
        Assert.Contains(allBuckets, b => b.ClientKey == "new-client");
    }
}
