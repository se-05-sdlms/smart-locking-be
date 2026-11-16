# Reconciliation history persistence (#248)

Assigning, responding to or synchronizing an existing reconciliation must insert
its new history event in the same transaction as the case, audit and notification.
Appending an event with a preassigned GUID to an already tracked navigation made
EF treat it as an existing row; real PostgreSQL updates failed with
`DbUpdateConcurrencyException` because that event row did not exist.

`AddEvent` now explicitly registers the event as Added and links its parent. This
also preserves relationship fixup for the returned history and newly created
cases. Status rules, payment verification and provider calls are unchanged.

Four migrated disposable PostgreSQL regressions cover creation/retry, existing
case assignment/retry, all three manual response statuses, and a matching PENDING
provider response. The original source failed the three existing-case tests.
Read-back checks history, audit, notification and unchanged unpaid subscription;
manual review never grants payment benefits. The creation and assignment retries
retain one event each. A fake provider supplies PENDING data; no real gateway,
payment, shared database or deployment is involved.
