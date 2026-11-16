# Atomic policy activation (#216)

Activating a replacement policy first saves the deactivation of the previous
policy, then activates the replacement and records the activation audit, all
within the existing transaction and PostgreSQL allocation advisory lock.
This avoids EF update ordering violating the immediate unique active-policy
index. The unique index remains enabled and no schema changes are needed.

A failure in the second save rolls back the first save; other database readers
continue to see the committed policy. Concurrent activation requests serialize
under the existing lock, leaving exactly one active policy. Existing coverage
reapproval and notifications remain in the same transaction.

Three regression cases run the actual service against migrated disposable
PostgreSQL databases: replacement and audit read-back, injected second-save
failure and recovery, and concurrent replacements. This is service/persistence
evidence; these cases do not perform browser login or shared database changes.
