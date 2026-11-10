using Microsoft.EntityFrameworkCore;
using SmartLocking.DemoSeeder;
using smart_locking_be.Infrastructure.Persistence;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection.");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseNpgsql(connectionString).Options);

await DemoSeederRunner.RunAsync(db, args);
