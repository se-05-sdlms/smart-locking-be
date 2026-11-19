using Microsoft.EntityFrameworkCore;
using smart_locking_be.Application.DTOs.Dashboards;
using smart_locking_be.Application.Forecast;
using smart_locking_be.Application.Interfaces.Services;
using smart_locking_be.Domain.Enums;
using smart_locking_be.Infrastructure.Persistence;

namespace smart_locking_be.Infrastructure.Services;

public sealed class AdminForecastService(ApplicationDbContext dbContext) : IAdminForecastService
{
    private const int LookbackDays = 90;
    private const int DefaultHorizonDays = 7;
    private const int HighRiskPercent = 85;
    private const int CriticalRiskPercent = 95;

    public async Task<AdminForecastResponse> GetForecastAsync(
        int horizonDays = DefaultHorizonDays,
        CancellationToken cancellationToken = default)
    {
        horizonDays = Math.Clamp(horizonDays, 3, 14);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset historyStart = now.AddDays(-LookbackDays);

        var lockers = await dbContext.OperationalLockers()
            .AsNoTracking()
            .Select(locker => new { locker.Id, locker.Code })
            .ToListAsync(cancellationToken);

        var capacities = await dbContext.OperationalCompartments()
            .AsNoTracking()
            .GroupBy(compartment => compartment.LockerId)
            .Select(group => new { LockerId = group.Key, Capacity = group.Count() })
            .ToDictionaryAsync(item => item.LockerId, item => item.Capacity, cancellationToken);

        var parcels = await dbContext.Parcels
            .AsNoTracking()
            .Where(parcel =>
                parcel.StoredAt >= historyStart ||
                (parcel.RetrievedAt.HasValue && parcel.RetrievedAt.Value >= historyStart) ||
                parcel.Status == ParcelStatus.Stored ||
                parcel.Status == ParcelStatus.Overdue)
            .Select(parcel => new
            {
                parcel.Status,
                parcel.StoredAt,
                parcel.RetrievedAt,
                LockerId = parcel.DeliveryRequest.LockerId
            })
            .ToListAsync(cancellationToken);

        var activeByLocker = parcels
            .Where(parcel => parcel.Status is ParcelStatus.Stored or ParcelStatus.Overdue)
            .GroupBy(parcel => parcel.LockerId)
            .ToDictionary(group => group.Key, group => group.Count());

        var incomingByLockerAndDay = parcels
            .Where(parcel => parcel.StoredAt >= historyStart)
            .GroupBy(parcel => (parcel.LockerId, Date: DateOnly.FromDateTime(parcel.StoredAt.UtcDateTime)))
            .ToDictionary(group => group.Key, group => group.Count());

        var pickedUpByLockerAndDay = parcels
            .Where(parcel => parcel.RetrievedAt.HasValue && parcel.RetrievedAt.Value >= historyStart)
            .GroupBy(parcel => (parcel.LockerId, Date: DateOnly.FromDateTime(parcel.RetrievedAt!.Value.UtcDateTime)))
            .ToDictionary(group => group.Key, group => group.Count());

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var dailyForecast = new List<DailyForecastResponse>(horizonDays);
        var projectedOccupiedByLocker = lockers.ToDictionary(
            locker => locker.Id,
            locker => activeByLocker.GetValueOrDefault(locker.Id));
        var peakOccupiedByLocker = projectedOccupiedByLocker.ToDictionary(
            item => item.Key,
            item => item.Value);

        for (int offset = 1; offset <= horizonDays; offset++)
        {
            DateOnly date = today.AddDays(offset);
            bool demandEvent = ForecastCalendar.IsDemandEventWindow(date);
            double multiplier = ForecastCalendar.GetDemandMultiplier(date);
            int totalIncoming = 0;
            int totalPickedUp = 0;

            foreach (var locker in lockers)
            {
                int capacity = capacities.GetValueOrDefault(locker.Id);
                int incoming = ForecastCount(incomingByLockerAndDay, locker.Id, date, multiplier);
                int pickedUp = ForecastCount(pickedUpByLockerAndDay, locker.Id, date, 1.0);
                totalIncoming += incoming;
                totalPickedUp += pickedUp;
                projectedOccupiedByLocker[locker.Id] = Math.Clamp(
                    projectedOccupiedByLocker[locker.Id] + incoming - pickedUp,
                    0,
                    capacity);
                peakOccupiedByLocker[locker.Id] = UpdatePeak(
                    peakOccupiedByLocker[locker.Id],
                    projectedOccupiedByLocker[locker.Id]);
            }

            int totalCapacity = capacities.Values.Sum();
            int occupied = projectedOccupiedByLocker.Values.Sum();
            dailyForecast.Add(new DailyForecastResponse(
                date,
                totalIncoming,
                totalPickedUp,
                occupied,
                Percentage(occupied, totalCapacity),
                demandEvent));
        }

        var atRiskLockers = lockers
            .Select(locker =>
            {
                int capacity = capacities.GetValueOrDefault(locker.Id);
                int current = activeByLocker.GetValueOrDefault(locker.Id);
                int forecast = peakOccupiedByLocker.GetValueOrDefault(locker.Id);
                int percent = Percentage(forecast, capacity);
                string risk = GetRiskLevel(percent);
                string reason = ForecastCalendar.IsDemandEventWindow(today.AddDays(1))
                    ? "Nhu cầu dự kiến tăng sau ngày sự kiện mua sắm."
                    : "Dựa trên đỉnh sử dụng dự kiến trong kỳ và lịch sử giao nhận.";

                return new LockerForecastResponse(
                    locker.Id,
                    locker.Code,
                    current,
                    capacity,
                    forecast,
                    percent,
                    risk,
                    reason);
            })
            .Where(item => item.RiskLevel != "normal")
            .OrderByDescending(item => item.ForecastOccupancyPercent)
            .Take(6)
            .ToList();

        int totalOperational = capacities.Values.Sum();
        int currentOccupied = activeByLocker.Values.Sum();
        int peakOccupied = dailyForecast.Count == 0 ? currentOccupied : dailyForecast.Max(item => item.ForecastOccupiedCompartments);

        return new AdminForecastResponse(
            now,
            horizonDays,
            currentOccupied,
            totalOperational,
            peakOccupied,
            Percentage(peakOccupied, totalOperational),
            dailyForecast,
            atRiskLockers);
    }

    private static int ForecastCount(
        IReadOnlyDictionary<(Guid LockerId, DateOnly Date), int> values,
        Guid lockerId,
        DateOnly targetDate,
        double multiplier)
    {
        int total = 0;
        int observedDays = 0;
        for (int offset = 7; offset <= LookbackDays; offset += 7)
        {
            DateOnly historicalDate = targetDate.AddDays(-offset);
            if (values.TryGetValue((lockerId, historicalDate), out int count))
                total += count;
            observedDays++;
        }

        return observedDays == 0 ? 0 : (int)Math.Round(total / (double)observedDays * multiplier, MidpointRounding.AwayFromZero);
    }

    private static int Percentage(int value, int total) =>
        total > 0 ? (int)Math.Round(value * 100d / total, MidpointRounding.AwayFromZero) : 0;

    internal static int UpdatePeak(int currentPeak, int projectedOccupied) =>
        Math.Max(currentPeak, projectedOccupied);

    internal static string GetRiskLevel(int occupancyPercent) =>
        occupancyPercent >= CriticalRiskPercent ? "critical" : occupancyPercent >= HighRiskPercent ? "high" : "normal";
}
