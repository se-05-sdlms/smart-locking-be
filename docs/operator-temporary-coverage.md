# Temporary operator coverage (#195)

This feature is an additive deployment. Apply the generated `AddTemporaryOperatorCoverage` migration through the normal deployment process; no migration has been applied to a shared database by this change. Existing lockers retain null coordinates; do not geocode or infer coordinates from Address. RecoveryAddress is independent and is never modified by location confirmation.

## Admin setup

`GET/PUT /api/lockers/{lockerId}/location` is Administrator only. PUT accepts `{ latitude, longitude, serviceArea }` and explicitly records the confirming Admin and a location revision. Coordinates must be finite and within latitude [-90,90], longitude [-180,180].

System policy create/update/read adds four optional fields, all null until configured: `operatorReplacementRadiusKm`, `operatorReplacementMaxLoad`, `operatorReplacementAreas` (array of service-area names), and `operatorReplacementEligibleUserIds` (explicit candidate group of operator IDs). Configure all four together; radius/load must be positive and lists nonempty. An active policy is immutable, as before. Suggestions require a currently active policy and confirmed coordinates in a configured area. Target and anchor must have the same service area. There are no invented radius, load or candidate defaults.

## API flow

- `GET /api/operator-absences`: Admin sees the latest 200 records; an Operator sees only their own.
- `POST /api/operator-absences`: `{ operatorUserId, startsAt, endsAt, reason }`. Operators report their own absence; Admin can record one for another operator. Instants are normalized to UTC and intervals are half open [start,end). Overlapping open absences for one operator are rejected. Creation produces suggestions only, including when Admin creates it.
- `POST /api/operator-absences/{id}/proposals`: Admin refreshes pending/unassigned proposals. Approved/active records are preserved.
- `POST /api/operator-absences/proposals/{id}/approve`: Admin supplies `{ operatorUserId?, reason }`. Omit operatorUserId to approve the displayed suggestion; provide an ID for an explicit manual selection/change. Manual selections pass the same eligibility, radius and whole-interval capacity checks. Changed automatic suggestions return NeedsApproval with refreshed evidence; review and explicitly approve again.
- `POST /api/operator-absences/{id}/cancel`: `{ reason }`. Operators can cancel their own unapproved absence. Cancellation of approved coverage requires Admin. It revokes all temporary rights immediately. To extend an interval, create an adjacent new absence with separate proposals/approval; intervals are never silently widened.

Proposal statuses: PendingApproval, Approved, Active, NeedsApproval, Unassigned, Expired, Cancelled. Responses include original operator, selected operator, original anchor locker, geographic distance in km, area, policy version ID, peak load before/after, approving Admin and activation time. Reasons explain missing prerequisites. Location changes invalidate the selection revision; no address, private employee location or temporary assignment is an anchor.

## Allocation and authorization

Each candidate must be an active LockerOperator in the explicitly configured group and available for the entire proposed interval. Anchors come only from the candidate's original active base assignments before the batch, with confirmed coordinates. Haversine distance is advisory geographic distance, not road time. Selection orders by distance, then whole-interval peak load, then stable operator ID. The peak load includes original managed lockers, all overlapping approved/active substitutes, and allocations already proposed in the same batch. Nonoverlapping reservations are not summed. Approval and activation serialize through a PostgreSQL transaction advisory lock, including across API/worker processes. Pending proposals reserve no rights; approval rechecks capacity.

The worker revalidates approved proposals when their start arrives and requires reapproval when conditions changed. It also suspends active coverage if it becomes ineligible and reconciles completion and newly added base lockers. A late worker can delay activation, but cannot grant rights before start or after end. Every read/action and notification/realtime recipient uses `EffectiveOperatorAssignments(at)` with exact UTC interval checks. Original base assignment rows and their unique-active-locker constraint remain intact; original operators lose rights during their absence, and temporary assignments never acquire Admin rights. Expiration/cancellation restores only a still active, available original operator; otherwise Admin is notified. In-app notices describe the locker and interval to both operators and Admin when attention is needed.

Locker realtime broadcasts resolve current authorized operators and active Admins on every publication and send through authenticated user groups. A previously subscribed socket is never used as a persistent locker entitlement, preventing stale subscriptions from retaining events after a handover.

Original assignment administration/history in UserService stays distinct from effective permissions. Future support/task/QR endpoints must use the same query helper rather than base assignment navigation collections.

## Validation boundary

Existing checks only; no test, fixture, snapshot or test script was added/changed. Build/format and the existing suite are recorded in the PR. Migration SQL is generated for review only. Runtime approval/concurrency/device integration still needs verification on a database with this additive migration and Admin-confirmed configuration; no real coordinates or operational assignments are invented during development.
