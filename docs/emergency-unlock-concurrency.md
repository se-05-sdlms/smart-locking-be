# Emergency unlock concurrency (#231)

`POST /api/emergency-unlocks` takes a PostgreSQL compartment row lock with
`FOR NO KEY UPDATE NOWAIT` after scope and incident validation, before recording an intent
or dispatching `OPEN`. An overlapping request for the same compartment receives
HTTP 409 with problem code `locker/unlock_in_progress`; it publishes no command
and creates no emergency/access records. Other compartments remain independent.

The transaction holds the lock through dispatch and persistence. Completion,
dispatch failure, SQL failure and cancellation release the owned transaction lock.
There is no cooldown: an intentional request after completion can open again.
The HTTP service owns its transaction; an internal caller supplying an existing
transaction owns its commit/rollback and must roll it back after a lock conflict.
In-memory EF tests do not provide this concurrency guarantee.

The lock permits foreign-key `KEY SHARE` checks because this operation does not
change compartment keys. A stronger `FOR UPDATE` lock formed a real PostgreSQL
deadlock with allocation (#244): the allocator held the parent `Locker` row while
its reservation FK waited on the compartment; emergency persistence then needed
the parent row for its own FK checks. The compatible lock removes that cycle and
still rejects overlapping emergency requests for the same compartment.

MQTT publishing and the SQL commit are not one atomic operation. If publishing
succeeds but persistence fails, SQL records roll back while the command may have
reached the device. The backend does not automatically retry that command. This
change prevents overlapping emergency requests from publishing twice; it does not
provide exactly-once hardware execution or prove a door opened. MQTT QoS 1 may
redeliver independently; device command acknowledgement remains external firmware
scope (#160). No physical locker was used for this regression verification.

Seven PostgreSQL regression cases use migrated, disposable databases and a barrier
at the transport boundary: same-compartment conflict, sequential opens,
independent compartments, failed dispatch, rollback after publish, and invalid
scope/compartment rejection, plus concurrent actual compartment allocation. The
cross-flow regression reproduced SQLSTATE `40P01` before the lock-mode fix, after
observing a real PostgreSQL lock wait; both operations succeed after the fix.
Actual service/persistence code runs in these tests.
