# SDLMS Code First Persistence Design

## Purpose

Implement the SDLMS database contract from `E:\CapstoneProject\docs\database_description.md` in the backend repository using EF Core Code First and the existing Clean Architecture projects.

## Sources And Priority

1. `database_description.md` defines the schema contract.
2. `SDLMS_Report3_SRS_User_Requirements_VI_Descriptions.md` confirms actors, workflows, and lifecycle.
3. `Report1_Project Introduction_approved_v1.2.md` confirms scope and exclusions.

No entity, persisted field, relationship, or business restriction may be invented from a generic locker design. In particular, Resident has no Building relationship, Shipper has no permanent User account, and DeliveryRequest remains separate from Parcel.

## Architecture

- `smart-locking-be.Domain` contains POCO entities and business-specific enums only.
- `smart-locking-be.Infrastructure` contains EF Core configuration, PostgreSQL-specific partial indexes, and `ApplicationDbContext`.
- `smart-locking-be.Tests` verifies the EF model contract through metadata without connecting to a database.
- Domain does not reference Entity Framework Core or Npgsql.

All persisted entities expose exactly the documented scalar fields. Navigation properties are added only for documented relationships. No generic audit fields, soft-delete fields, tenant fields, or concurrency fields are introduced.

## Base Configuration

Infrastructure provides `BaseConfiguration<TEntity>`. It applies the PascalCase table name, the Guid primary key supplied by each derived configuration, and shared helpers for unconstrained PostgreSQL `character varying`, `text`, and string-backed enum columns. Every entity still has a dedicated configuration class containing its own relationships, indexes, filters, checks, and precision rules.

The base is persistence-only. Domain entities do not inherit an EF-aware base type.

## Type Mapping

- `uuid` maps to `Guid`.
- `varchar` maps to `string` with PostgreSQL `character varying` and no invented maximum length.
- `text` maps to `string` with PostgreSQL `text`.
- `boolean` maps to `bool`.
- `int` maps to `int`.
- monetary/rate `decimal` maps to `decimal(18,2)`.
- `date` maps to nullable `DateOnly` where documented optional.
- `timestamp` maps to `DateTimeOffset` and PostgreSQL `timestamp with time zone`, with UTC semantics.
- Documented enum value sets map to business-specific C# enums persisted as strings.
- Undefined value sets such as SizeCategory, notification/incident Type, EventType in NotificationRule, and history ActionType remain strings.

## Relationships And Delete Behavior

All documented foreign keys are explicit. Required and optional cardinality follows the Required column and relationship descriptions. One-to-one relationships are enforced with unique indexes where documented. All relationships use `DeleteBehavior.Restrict`; lifecycle termination uses status, `IsActive`, `RevokedAt`, or `EffectiveTo` rather than cascaded deletion.

## Database Constraints

Every documented named index is created explicitly. PostgreSQL partial unique indexes cover nullable login identifiers, active biometric enrollment, active operator scopes, the active system policy, active compartment allocation, and non-null provider transaction IDs.

Database-safe checks cover login identifier presence, non-negative attempts/durations/rates, positive policy expiry/storage duration and payment amount, exactly one operator scope, parcel deadline order and mutually exclusive retrieval/removal, overdue calculation order, read timestamp consistency, and required completion timestamps for resolved incidents and closed maintenance requests. Rules requiring another table or a user's role remain application/domain concerns.

## Verification

Model-contract tests inspect all 29 entity types for property presence, nullability and CLR types, table names, primary keys, enum storage, indexes and filters, check constraints, foreign keys, cardinality, and delete behavior. Verification then runs the full test suite and solution build. Migration generation uses the Infrastructure project with API as startup and does not update any database.

## Assumptions

- Unbounded `varchar` is represented as PostgreSQL `character varying` without a length limit.
- All documented timestamps represent instants and use `DateTimeOffset`/`timestamp with time zone`.
- EF/Npgsql client-side Guid generation is retained; no database UUID extension/default is introduced.
- `decimal(18,2)` follows the database document's Code First recommendation.
- SizeCategory remains a string because the documents explicitly do not define its allowed values.
- The initial migration is named `InitialCreate` and is generated only after the model tests and build pass.
