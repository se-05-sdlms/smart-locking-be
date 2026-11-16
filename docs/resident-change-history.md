# Resident account change history (#229)

New history events are explicitly added to the event DbSet with their request
foreign key and navigation. An event with an assigned GUID appended to a tracked
existing request's collection could otherwise be discovered as Modified. EF then
issued an UPDATE for an event that did not exist, raising
DbUpdateConcurrencyException and rolling back the review.

The existing transaction and resident lock remain in place. History, identity,
audit and in-app notification commit together. Terminal review/cancel retries
continue to return their existing result without another event or notification.
Approval blockers still leave the request Submitted and preserve the resident's
registered locker.

Four regressions against the unchanged dev source failed on migrated disposable
PostgreSQL databases: approval from a null original locker, rejection, blocked
approval and cancellation. The approval failure reproduced the reported
DbUpdateConcurrencyException in ReviewAsync/SaveAsync. All four passed with the
explicit Add. A fifth regression checks concurrent approvals with fresh contexts,
one identity update and no duplicate applied event, audit or notification.

These tests call the real service and read persisted rows using fresh contexts.
They do not prove browser/native account-change journeys or provider delivery.
No shared application database, schema, notification provider or physical locker
is modified. This patch selectively ports only the history insertion change
from the pre-existing local patch; that worktree and its untracked tests remain
untouched.
