using smart_locking_be.Infrastructure.Services;

namespace smart_locking_be.Tests.Parcels;

public sealed class ParcelTimelineTests
{
    [Fact]
    public void GetDeadlines_StartsAtNextVietnamMidnight_AndUsesSevenPlusSevenDays()
    {
        DateTimeOffset depositedMonday = new(2026, 10, 5, 16, 30, 0, TimeSpan.FromHours(7));

        var (countStart, pickupDue, transferEligible) = ParcelTimeline.GetDeadlines(
            depositedMonday,
            freeStorageHours: 7 * 24,
            maxStorageHours: 14 * 24);

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(7)), countStart.ToOffset(TimeSpan.FromHours(7)));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 0, 0, 0, TimeSpan.FromHours(7)), pickupDue.ToOffset(TimeSpan.FromHours(7)));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(7)), transferEligible.ToOffset(TimeSpan.FromHours(7)));
    }
}
