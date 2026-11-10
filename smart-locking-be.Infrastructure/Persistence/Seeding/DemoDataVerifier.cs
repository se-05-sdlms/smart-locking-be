using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace smart_locking_be.Infrastructure.Persistence.Seeding;

public static class DemoDataVerifier
{
    public static async Task<SortedDictionary<string, long>> VerifyAsync(ApplicationDbContext db, string[] tables)
    {
        var counts = new SortedDictionary<string, long>();
        await using var command = new NpgsqlCommand(string.Join(" UNION ALL ", tables.Select(table =>
            $"SELECT '{table}' AS name, count(*) FROM public.\"{table}\"")),
            (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) counts[reader.GetString(0)] = reader.GetInt64(1);
        }
        var missing = counts.Where(item => item.Value < 10).Select(item => $"{item.Key}={item.Value}").ToArray();
        if (missing.Length > 0) throw new Exception("Tables below 10 rows: " + string.Join(", ", missing));
        await VerifyRelationshipsAsync(db);
        return counts;
    }

    private static async Task VerifyRelationshipsAsync(ApplicationDbContext db)
    {
        var checks = db.Model.GetEntityTypes().SelectMany(type => type.GetForeignKeys()).Select((fk, index) =>
        {
            string present = string.Join(" AND ", fk.Properties.Select(property => $"d.\"{property.Name}\" IS NOT NULL"));
            string join = string.Join(" AND ", fk.Properties.Zip(fk.PrincipalKey.Properties,
                (dependent, principal) => $"d.\"{dependent.Name}\" = p.\"{principal.Name}\""));
            return $"SELECT 'foreign_key_{index}' AS name, count(*) FROM public.\"{fk.DeclaringEntityType.GetTableName()}\" d " +
                $"LEFT JOIN public.\"{fk.PrincipalEntityType.GetTableName()}\" p ON {join} WHERE {present} AND p.\"{fk.PrincipalKey.Properties[0].Name}\" IS NULL";
        }).ToList();
        Dictionary<string, string> businessChecks = new()
        {
            ["resident_role"] = "SELECT count(*) FROM \"ResidentProfile\" r JOIN \"User\" u ON r.\"UserId\"=u.\"Id\" WHERE u.\"Role\" <> 'Resident'",
            ["operator_role"] = "SELECT count(*) FROM \"OperatorAssignment\" a JOIN \"User\" u ON a.\"OperatorUserId\"=u.\"Id\" WHERE u.\"Role\" <> 'LockerOperator'",
            ["delivery_compartment_locker"] = "SELECT count(*) FROM \"DeliveryRequest\" d JOIN \"LockerCompartment\" c ON d.\"AllocatedCompartmentId\"=c.\"Id\" WHERE d.\"LockerId\"<>c.\"LockerId\"",
            ["return_compartment_locker"] = "SELECT count(*) FROM \"ReturnRequest\" r JOIN \"LockerCompartment\" c ON r.\"AllocatedCompartmentId\"=c.\"Id\" WHERE r.\"LockerId\"<>c.\"LockerId\"",
            ["parcel_terminal_timestamp"] = "SELECT count(*) FROM \"Parcel\" WHERE (\"Status\"='Retrieved' AND \"RetrievedAt\" IS NULL) OR (\"Status\"='Removed' AND \"RemovedAt\" IS NULL) OR (\"Status\" IN ('Stored','Overdue') AND (\"RetrievedAt\" IS NOT NULL OR \"RemovedAt\" IS NOT NULL))",
            ["pickup_release"] = "SELECT count(*) FROM \"Parcel\" p JOIN \"DeliveryRequest\" d ON p.\"DeliveryRequestId\"=d.\"Id\" WHERE p.\"Status\"='Retrieved' AND d.\"CompartmentReleasedAt\" IS DISTINCT FROM p.\"RetrievedAt\"",
            ["paid_charge"] = "SELECT count(*) FROM \"OverdueCharge\" WHERE \"Status\"='Paid' AND \"PaidAt\" IS NULL",
            ["successful_payment"] = "SELECT count(*) FROM \"PaymentTransaction\" p JOIN \"OverdueCharge\" c ON p.\"OverdueChargeId\"=c.\"Id\" WHERE p.\"Status\"='Success' AND (p.\"CompletedAt\" IS NULL OR c.\"Status\"<>'Paid' OR p.\"Amount\"<>c.\"Amount\" OR p.\"Currency\"<>c.\"Currency\")",
            ["subscription_snapshot"] = "SELECT count(*) FROM \"ResidentSubscription\" WHERE \"Status\"='Active' AND (\"StartDate\">=\"EndDate\" OR \"PaidAt\" IS NULL OR \"BenefitsSnapshotRecordedAt\" IS NULL OR \"MaxActiveParcelsSnapshot\" IS NULL OR \"MaxActiveParcelsSnapshot\"<=0)",
            ["task_source"] = "SELECT count(*) FROM \"OperatorTaskHandover\" h LEFT JOIN \"Incident\" i ON h.\"TaskId\"=i.\"Id\" WHERE h.\"TaskKind\"='incident' AND (i.\"Id\" IS NULL OR i.\"LockerId\"<>h.\"LockerId\")",
            ["return_cancellation_owner"] = "SELECT count(*) FROM \"ReturnCancellationRequest\" c JOIN \"ReturnRequest\" r ON c.\"ReturnRequestId\"=r.\"Id\" JOIN \"ResidentProfile\" p ON r.\"ResidentProfileId\"=p.\"Id\" WHERE c.\"ResidentUserId\"<>p.\"UserId\"",
            ["maintenance_incident_scope"] = "SELECT count(*) FROM \"MaintenanceRequest\" m JOIN \"Incident\" i ON m.\"IncidentId\"=i.\"Id\" WHERE m.\"LockerId\"<>i.\"LockerId\" OR (m.\"Status\"='Closed' AND (m.\"ClosedAt\" IS NULL OR i.\"Status\"<>'Resolved'))"
        };
        checks.AddRange(businessChecks.Select(item => $"SELECT '{item.Key}' AS name, ({item.Value}) AS count"));
        await using var command = new NpgsqlCommand(string.Join(" UNION ALL ", checks),
            (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (reader.GetInt64(1) != 0) throw new Exception($"Data integrity check failed: {reader.GetString(0)}={reader.GetInt64(1)}");
    }
}
