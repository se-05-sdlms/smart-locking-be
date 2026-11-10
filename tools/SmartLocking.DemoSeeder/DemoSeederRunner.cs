using Microsoft.EntityFrameworkCore;
using smart_locking_be.Infrastructure.Persistence;
using smart_locking_be.Infrastructure.Persistence.Seeding;
using System.Text.Json;

namespace SmartLocking.DemoSeeder;

internal static class DemoSeederRunner
{
    public static async Task RunAsync(ApplicationDbContext db, string[] args)
    {
        await db.Database.OpenConnectionAsync();
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().Order().ToArray();
        bool createdDataset = false;
        if (tables.Length != 41) throw new Exception($"Expected 41 project tables, found {tables.Length}; review the dataset.");
        if (args.Length != 1 || args[0] is not ("--verify" or "--dry-run" or "--apply"))
            throw new ArgumentException("Use --verify, --dry-run (rollback), or --apply (commit).");
        if (args[0] != "--verify")
        {
            var password = Environment.GetEnvironmentVariable("BOXORA_DEMO_PASSWORD");
            if (string.IsNullOrEmpty(password) || password.Length < 16)
                throw new ArgumentException("Set BOXORA_DEMO_PASSWORD to a private password of at least 16 characters.");
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(20261110, 41)");
            var adminId = DemoDataSeeder.AdminId;
            if (await db.Users.AnyAsync(user => user.Id == adminId))
            {
                await DemoDataVerifier.VerifyAsync(db, tables);
                Console.WriteLine("Dataset already exists; verified without inserting duplicates.");
            }
            else
            {
                createdDataset = true;
                await DemoDataSeeder.SeedAsync(db, password);
                await db.SaveChangesAsync();
                await DemoDataVerifier.VerifyAsync(db, tables);
                if (args[0] == "--apply")
                {
                    await transaction.CommitAsync();
                    Console.WriteLine("COMMITTED demo dataset.");
                }
                else Console.WriteLine("DRY RUN passed; rolling back all inserts.");
            }
        }
        db.ChangeTracker.Clear();
        if (args[0] == "--dry-run")
        {
            var adminId = DemoDataSeeder.AdminId;
            if (createdDataset && await db.Users.AnyAsync(user => user.Id == adminId))
                throw new Exception("Dry-run rollback verification failed.");
            Console.WriteLine(createdDataset
                ? "PASS: dry-run inserts were rolled back."
                : "PASS: existing dataset verified without writing rows.");
            return;
        }
        var counts = await DemoDataVerifier.VerifyAsync(db, tables);
        foreach (var item in counts) Console.WriteLine($"{item.Key}: {item.Value}");
        Console.WriteLine($"Verified {counts.Count} project tables, {counts.Values.Sum()} total rows.");
        var reportPath = Environment.GetEnvironmentVariable("BOXORA_DEMO_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { checkedAt = DateTimeOffset.UtcNow, counts }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
