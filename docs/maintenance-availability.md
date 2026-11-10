# Maintenance and operational availability

Refs #197. Existing maintenance endpoints and DTOs remain compatible.

- `Open` and `InProgress` requests block their compartment; a null compartment blocks the entire locker.
- `Closed` still blocks while its linked incident is unresolved. Unresolved equipment incidents (`Locker` or `Compartment`, case insensitive) independently block their affected scope. Other incident types do not disable equipment unless linked to maintenance.
- `Cancelled` removes only that request's cause. It does not remove a physical incident, another request, an inactive state or the administrator's manual block.
- Persisted `OperationalStatus` remains the manual setting. Locker detail/list, compartment responses and dashboard availability overlay operational causes: an otherwise Operational scope is exposed as OutOfService while blocked. Parent blocks also apply to children. Setting the manual status to Operational cannot bypass maintenance.
- Allocation and ordinary unlock use the same availability query. Existing parcels, reservations, history and door sensor state are preserved. An explicitly authorized emergency workflow retains its existing manual-status and incident/authorization checks and can operate during maintenance.
- Maintenance mutations and allocations lock the same locker row in their transaction before availability decisions. Allocation uses READ COMMITTED so a request committed while waiting for the row lock is visible to the subsequent query.
- Allowed changes: Open -> InProgress/Closed/Cancelled; InProgress -> Closed/Cancelled. Terminal requests cannot reopen. Repeating the current state is idempotent. Closing requires a resolution summary; cancellation requires notes. Invalid status/priority values are rejected.
- `MaintenanceChanged` is published to the locker operations channel after persistence; clients should refresh maintenance and locker availability. Activity and audit history identify the actor and change.

No schema migration, dependency, device command or occupancy reset is introduced. Temporary Operator authorization is integrated by #195's common effective-assignment query; until that dependency is present, this branch retains the current assignment checks. FE #90 is implemented separately.
