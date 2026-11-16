# Operator assignment preconditions (#218, #219)

Assigning an existing operator requires an Active operator account and an existing
locker. Disabled or Locked operators receive 409 `user/inactive`; an unknown
locker receives 404 `locker/not_found`, before any assignment or creation audit
is persisted. An empty locker ID retains its existing 400 validation contract.

The existing PostgreSQL allocation advisory lock still serializes competing
assignments. The operator row is locked before reading its status; the read is
untracked so a previously tracked user cannot supply stale status. A status
update already in progress commits before validation; if that update disables
the operator, no new assignment is created. Disabling an account after a valid
assignment commits is a separate account-state transition and does not erase
historical assignments.

Six migrated disposable PostgreSQL regressions cover Disabled/Locked rejection,
unknown locker rejection, successful assignment with audit read-back, concurrent
duplicate assignment conflict, and a concurrent disable transaction preceding
assignment validation. The unchanged source fails the first three cases while
the success and duplicate-allocation cases already pass. The concurrency case
uses an actual PostgreSQL lock wait and separate connections.

This change affects the existing-operator assignment endpoint only. Creating a
new operator with an initial locker (#221) and email syntax (#220) remain
separate tasks. Tests use the real service and no email, shared database or
device. Browser/JWT authorization acceptance is not claimed by these tests.
