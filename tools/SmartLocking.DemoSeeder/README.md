# Demo data

Creates ten linked demo scenarios covering all 41 project tables. No existing rows are deleted or updated. IDs are deterministic, so applying the same dataset twice verifies it without duplicating rows.

Responsibilities follow the existing layer boundaries:
- `Infrastructure/Persistence/Seeding/DemoDataSeeder.cs` builds the entity graph using the application's existing hash services.
- `Infrastructure/Persistence/Seeding/DemoDataVerifier.cs` checks persisted table counts and database relationships.
- `DemoSeederRunner.cs` handles console arguments, transaction/commit/rollback, environment settings, and report output.
- `Program.cs` only creates the database context and starts the runner. Domain and Application have no dependency on the console tool or seeding implementation.

Set `ConnectionStrings__DefaultConnection` and `BOXORA_DEMO_PASSWORD` privately in the environment. The password must have at least 16 characters. Never commit either value. Optionally set `BOXORA_DEMO_REPORT` to an absolute path for the count report.

```powershell
dotnet run --project tools/SmartLocking.DemoSeeder -- --dry-run
dotnet run --project tools/SmartLocking.DemoSeeder -- --apply
dotnet run --project tools/SmartLocking.DemoSeeder -- --verify
```

The dry run saves and checks the dataset inside a transaction, then rolls it back. Apply commits only after all tables have at least ten rows and relationship checks pass. Verify performs read-only checks.

Accounts: `demo.admin@boxora.example`, `demo.operator01@boxora.example` through `10`, `demo.backup01@boxora.example` through `10`, and `demo.resident01@boxora.example` through `10`. All use the supplied private demo password, hashed separately with the application's password service.

Lockers are explicitly offline and uncommissioned; this tool never sends hardware commands, emails, SMS, push notifications, or payment requests. Payment records are fictional and have no real gateway identifiers. OTPs and refresh tokens are expired/revoked; biometric references are fictional and revoked; push installations are inactive. Sample data does not configure integrations or simulate a physical locker connection.
