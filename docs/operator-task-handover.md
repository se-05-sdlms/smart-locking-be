# Effective operator work and handover

Depends on #195 effective permissions and #153 shared incident SLA target selection. No separate SLA monitor or work clones are created.

`GET /api/operator-tasks?offset=0&limit=30&lockerId=...` returns open incident and maintenance records plus stored/overdue parcels whose maximum storage time has passed. Administrators see all lockers; operators see only currently effective lockers, including temporary cover. Results sort deterministically by priority then creation time, source ID and kind. Pagination limits are 1–100 and offsets 0–10000. `sourcePath` points to the original authorized API resource. Maintenance has no invented due date. Incident due dates use the existing SLA priority and current effective policy/options, with acknowledgement deadline while unassigned/open and resolution deadline otherwise. Recovery due date is the original maximum storage time; recovery disappears after transfer/removal/pickup. No work after collection-point transfer is tracked here.

`GET /api/operator-tasks/{kind}/{id}/handovers` reads history, including after the source work is closed. Kind is `incident`, `maintenance` or `recovery`.

`POST /api/operator-tasks/{kind}/{id}/handovers` appends required `workDone`, optional `blockers`, required `nextSteps` (each up to 2000 characters), and optional `intendedReceiverUserId`. A receiver, if specified, must currently have effective permission at that locker. Only open work accepts a new handover. IDs and names of authors are preserved at creation.

`POST /api/operator-tasks/handovers/{id}/acknowledgements` appends acknowledgement and optional notes (up to 1000 characters) for the currently authorized receiving operator or Administrator. Authors cannot acknowledge their own handover. Repeating the request by the same receiver returns the original acknowledgement without changing it. A new current operator may acknowledge old history even if an earlier intended receiver has since lost permission.

Assignment/coverage updates and handover writes share the #195 PostgreSQL advisory transaction lock, so receiver eligibility is checked in the same serialized transaction as insertion. Reads resolve scope at request time. There are no update/delete endpoints. PostgreSQL triggers reject UPDATE/DELETE on handover and acknowledgement history, preserving authors and contents after assignment changes. Original task status changes remain in their own workflows.

Migration is additive and independent, with no data backfill or shared DB application. The polymorphic source reference is validated against the original record and locker before writes; history never changes the original task owner or status.
