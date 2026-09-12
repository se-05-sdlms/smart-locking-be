# SDLMS Code First Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the complete documented SDLMS EF Core Code First model and make it ready for an initial PostgreSQL migration.

**Architecture:** Domain owns pure POCO entities and business enums. Infrastructure owns a reusable `BaseConfiguration<TEntity>`, one explicit configuration per entity, PostgreSQL constraints, and the existing DbContext. Tests inspect the real EF model metadata.

**Tech Stack:** .NET 8, C# 12, EF Core 8.0.11, Npgsql 8.0.11, PostgreSQL 15+, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-09-12-sdlms-code-first-design.md`

## Global Constraints

- `database_description.md` is the schema source of truth.
- Domain must not reference EF Core, Infrastructure, API, or Npgsql.
- Do not create Resident-Building or permanent Shipper-User relationships.
- DeliveryRequest and Parcel remain separate with a one-to-zero-or-one relationship.
- Do not invent persisted fields or enum members.
- All documented indexes, filters, checks, foreign keys, and Restrict delete behaviors must be explicit.
- No database update, drop, or production connection is allowed.

---

### Task 1: Create The Executable Model Contract

**Files:**
- Modify: `smart-locking-be.Tests/smart-locking-be.Tests.csproj`
- Replace: `smart-locking-be.Tests/Test.cs`

**Interfaces:**
- Consumes: existing `ApplicationDbContext(DbContextOptions<ApplicationDbContext>)`.
- Produces: metadata assertions that name all 29 entities and their documented schema contracts.

- [ ] **Step 1: Add a project reference to Infrastructure**

```xml
<ProjectReference Include="..\smart-locking-be.Infrastructure\smart-locking-be.Infrastructure.csproj" />
```

- [ ] **Step 2: Write model metadata tests**

Create a real Npgsql-backed EF model without opening a connection. Assert literal entity/property, table, PK, FK/delete behavior, index/filter, and check-constraint expectations derived from the database document.

- [ ] **Step 3: Run the tests and verify RED**

Run: `dotnet test smart-locking-be.Tests/smart-locking-be.Tests.csproj --no-restore`

Expected: FAIL because the current model contains no SDLMS entity types.

### Task 2: Add Domain Enums And Entities

**Files:**
- Create: `smart-locking-be.Domain/Enums/*.cs`
- Create: `smart-locking-be.Domain/Entities/*.cs`

**Interfaces:**
- Consumes: the exact Fields, Relationships, and Status/Enum sections of the database document.
- Produces: 29 POCO entity types with Guid identifiers, documented scalar properties, and documented navigation properties.

- [ ] **Step 1: Add specific enums**

Create enums for User, approval, OTP, building/locker state, delivery request, parcel/access, payment, notification, incident, locker event, emergency unlock, maintenance, and audit result value sets. Keep undefined value sets as strings.

- [ ] **Step 2: Add all documented POCO entities**

Implement User, Role, Permission, UserRole, RolePermission, ResidentProfile, ResidentBiometric, OtpChallenge, Building, LockerCluster, Locker, LockerCompartment, OperatorAssignment, SystemPolicy, NotificationRule, DeliveryRequest, Parcel, ParcelStatusHistory, ParcelAccessEvent, OverdueCharge, PaymentTransaction, Notification, Incident, IncidentAction, LockerEvent, EmergencyUnlock, MaintenanceRequest, MaintenanceActivity, and AuditLog.

- [ ] **Step 3: Verify Domain compiles independently**

Run: `dotnet build smart-locking-be.Domain/smart-locking-be.Domain.csproj --no-restore`

Expected: PASS with no EF Core reference.

### Task 3: Add Base Configuration And Identity/Infrastructure Mapping

**Files:**
- Create: `smart-locking-be.Infrastructure/Persistence/Configurations/BaseConfiguration.cs`
- Create: dedicated configurations for User through LockerCompartment and OperatorAssignment.

**Interfaces:**
- Consumes: Domain entities and Npgsql EF Core metadata APIs.
- Produces: common table/key/type mapping plus explicit identity, RBAC, resident, OTP, building, cluster, locker, compartment, and assignment schemas.

- [ ] **Step 1: Implement BaseConfiguration**

The base receives `Expression<Func<TEntity, Guid>>`, maps `ToTable(typeof(TEntity).Name)`, configures the primary key, and exposes helpers for varchar, text, and string enum conversion.

- [ ] **Step 2: Implement entity configurations**

Add all required properties, relationships, named indexes, PostgreSQL filters, and checks for this group. Use `DeleteBehavior.Restrict` on every FK.

- [ ] **Step 3: Run model tests**

Run: `dotnet test smart-locking-be.Tests/smart-locking-be.Tests.csproj --no-restore`

Expected: still FAIL, but the implemented entity group is now present and mapped.

### Task 4: Add Policy, Delivery, Parcel, And Financial Mapping

**Files:**
- Create: configurations for SystemPolicy, NotificationRule, DeliveryRequest, Parcel, ParcelStatusHistory, ParcelAccessEvent, OverdueCharge, and PaymentTransaction.

**Interfaces:**
- Consumes: Task 2 domain types and Task 3 BaseConfiguration.
- Produces: policy snapshots, allocation uniqueness, parcel lifecycle, access history, overdue charge, and payment idempotency mappings.

- [ ] **Step 1: Implement configurations and relationships**

Map all literal index names and partial filters, `decimal(18,2)`, deadline/amount checks, one-to-one uniqueness, and Restrict foreign keys.

- [ ] **Step 2: Run model tests**

Run: `dotnet test smart-locking-be.Tests/smart-locking-be.Tests.csproj --no-restore`

Expected: still FAIL only for remaining unmapped groups or contract mismatches.

### Task 5: Add Notification, Incident, Operations, And Audit Mapping

**Files:**
- Create: configurations for Notification, Incident, IncidentAction, LockerEvent, EmergencyUnlock, MaintenanceRequest, MaintenanceActivity, and AuditLog.

**Interfaces:**
- Consumes: Task 2 domain types and Task 3 BaseConfiguration.
- Produces: remaining transaction/history relationships, indexes, checks, and Restrict delete behavior.

- [ ] **Step 1: Implement remaining configurations**

Map optional context foreign keys independently, keep AuditLog EntityId logical, and add resolved/closed/read consistency checks where enforceable within the row.

- [ ] **Step 2: Run model tests**

Run: `dotnet test smart-locking-be.Tests/smart-locking-be.Tests.csproj --no-restore`

Expected: mappings compile; tests may still fail until DbSet exposure and final contract corrections.

### Task 6: Complete DbContext And Reach GREEN

**Files:**
- Modify: `smart-locking-be.Infrastructure/Persistence/ApplicationDbContext.cs`

**Interfaces:**
- Consumes: all Domain entity types and assembly configurations.
- Produces: strongly typed DbSet properties for all 24 entities.

- [ ] **Step 1: Add every DbSet**

Expose one `DbSet<TEntity> => Set<TEntity>()` for each of the 29 documented entities while retaining `ApplyConfigurationsFromAssembly`.

- [ ] **Step 2: Run the model contract tests**

Run: `dotnet test smart-locking-be.Tests/smart-locking-be.Tests.csproj --no-restore`

Expected: PASS with all metadata assertions green.

- [ ] **Step 3: Refactor duplicate mapping code through BaseConfiguration helpers**

Keep entity-specific relationships and constraints in dedicated configuration files; rerun the model tests after refactoring.

### Task 7: Build, Test, And Generate Initial Migration

**Files:**
- Create: `smart-locking-be.Infrastructure/Persistence/Migrations/*InitialCreate*.cs`
- Create: `smart-locking-be.Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: the complete EF model.
- Produces: a reproducible PostgreSQL initial schema migration.

- [ ] **Step 1: Run full tests and build**

Run: `dotnet test smart-locking-be.sln --no-restore`

Run: `dotnet build smart-locking-be.sln --no-restore`

Expected: both commands exit 0 with no test failures or compilation errors.

- [ ] **Step 2: Generate migration without updating a database**

Run with a process-local placeholder connection string:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Database=sdlms_migration;Username=postgres;Password=postgres'
dotnet ef migrations add InitialCreate --project smart-locking-be.Infrastructure --startup-project smart-locking-be.API --output-dir Persistence/Migrations
```

Use an EF Core CLI 8.0.11 executable. Do not run `database update`.

- [ ] **Step 3: Inspect generated migration and verify idempotent model state**

Confirm all 29 tables, named indexes, filters, checks, and Restrict foreign keys are represented. Run tests and build again after migration generation.

### Task 8: Final Documentation Contract Review

**Files:**
- Review: `E:\CapstoneProject\docs\database_description.md`
- Review: all changed Domain, Infrastructure, Tests, and migration files.

**Interfaces:**
- Consumes: source-of-truth documentation and final diff.
- Produces: a completed per-entity checklist and assumption report.

- [ ] **Step 1: Check every entity against the documentation**

For each entity confirm fields, CLR types, nullability, enum, PK, FK, navigation, cardinality, indexes, filtered indexes, checks, and delete behavior.

- [ ] **Step 2: Run final verification**

Run: `dotnet test smart-locking-be.sln --no-restore`

Run: `dotnet build smart-locking-be.sln --no-restore`

Run: `git diff --check`

Expected: tests/build exit 0 and diff check emits no errors.
