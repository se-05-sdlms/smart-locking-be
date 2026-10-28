# Resident account changes

Authenticated Resident endpoints under `/api/resident-account-changes`:

- `GET /` (pageNumber/pageSize) and `GET /{id}` return only owned requests. Administrator can read all requests.
- `GET /locker-eligibility/{targetLockerId}` returns `canApply` and exact `blockers[{kind,id,message}]`. This is advisory; approval rechecks under a resident row lock.
- `POST /locker` accepts `{targetLockerId,reason}`. One pending request per resident/kind; retries for the same target return it. A request may wait while its displayed obligations are cleared.
- `POST /phone:request-otp` accepts `{newPhoneNumber,currentPassword}`. The configured SMS transport must accept the message; disabled/unavailable transport returns 503. No fake OTP is returned. One request/minute and five/hour per account, in addition to auth rate limits.
- `POST /phone` accepts `{newPhoneNumber,currentPassword,otpCode,reason}`. OTP is bound to this account, destination and `PhoneChange` purpose. Proof expires after 24 hours. Fresh verification renews an expired pending request. A retry after a lost successful response returns the pending request while its proof is still valid.
- `POST /{id}:cancel` cancels an owned Submitted request.
- Administrator `POST /{id}:review` accepts `{approve,reason}`. Both approval and rejection require a reason. A blocked approval returns the request still **Submitted**, with current blockers and an `ApprovalBlocked` history event. Clients must inspect `status`/`appliedAt`, not treat HTTP 200 as approval.

States are `Submitted`, `Applied`, `Rejected`, `Cancelled`. Responses include old/new values, creation/update/effective times, review reason, blockers and append-only processing history. Changes emit audit and in-app notifications. Phone approval invalidates existing access tokens through current-phone claim validation, revokes all refresh tokens and unused account OTPs, and requires login with the new number. Public guest endpoints retain their existing authorization behavior.

Locker approval blocks live parcels, incomplete physical recovery confirmation, all active return/delivery work (at any locker), unreleased allocations/reservations, debt, pending payments, unresolved payment reconciliation, and active/future benefits with unknown historical snapshots. A physically confirmed Removed parcel (`RemovedAt` + `RemovedByUserId`, #198) needs no later warehouse collection acknowledgement. System-wide purchased plans have no locker restriction; their snapshots and periods are never rewritten by a locker change.

Both locker and phone approval also block unfinished fulfillment, pending return cancellation, unconsumed offline pickup credentials and access sessions without a newer confirmed door closure. Historical verified closure resolves an old session even if another person later opens the same compartment. An `AccessSession` blocker identifies the access event for operator assistance, not a resident detail route. Phone approval must not leave a previously captured recipient number usable on pending delivery or offline access. Registration and locker changes use the maintenance-aware operational availability overlay and serialize with its parent-locker lock.

Source dependencies: purchased benefits #185, reconciliation #186, configured SMS #152. Combined integration also applies operational locker eligibility #197, physical recovery #198, resident-serialized return creation #166/#192, and unresolved offline credential protection #77. The independent migration creates only `ResidentAccountChange` and `ResidentAccountChangeEvent`; it must be reconciled with other additive model snapshots. No database update or external payment/SMS operation was performed during implementation.
