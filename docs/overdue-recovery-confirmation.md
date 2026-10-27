# Overdue recovery confirmation (#198)

`POST /api/parcels/{id}:transferToCollectionPoint` confirms that the authorized Operator/Administrator has physically delivered the parcel to the existing `Locker.RecoveryAddress`. The response remains `{ parcelId, parcelCode, collectionAddress, status }`; clients must display the recovery address and this meaning before submitting. No destination, warehouse or in-transit workflow is introduced.

A successful confirmation stores `Removed`, `RemovedAt`, `RemovedByUserId`, the status history, `DeliveryRequest.CompartmentReleasedAt`, outstanding reservations for that delivery, audit and queued notifications in one database transaction. Retrying an already confirmed transfer returns its existing result without another history, audit or notification. Authorization is still checked on retry.

Transfer, pickup command dispatch, both pickup completion paths and overdue status updates serialize on the PostgreSQL parcel row. A pickup command still awaiting a subsequent closed-door observation blocks transfer with `parcel/invalid_state`. Removed parcels cannot use old pickup credentials to dispatch another command, and late door telemetry cannot change them to Retrieved. Door/operational state remains unchanged; existing allocation guards still apply.

The PostgreSQL concurrency behavior requires deployment integration verification. Nonrelational test providers do not emulate database row locking. No new tests or migrations are included.

## Integration with open Bluetooth PR #166

This patch targets current `dev`. PR #166 currently replaces both transfer endpoints with a reset request and marks the parcel Removed after an ESP reset ACK. Its reset transport, retry and offline-PIN revocation should be retained, but its eventual rebase must preserve the Operator's explicit confirmation of delivery to `RecoveryAddress`. A reset/door ACK proves device processing, not physical arrival at that address. Its reset completion writer also needs the same parcel lock and must not overwrite a competing terminal pickup. Keep device cleanup pending/allocation protection distinct from the business confirmation; do not report offline PIN revocation based on this dev-only patch.

#77 remains open pending PR #166 integration and physical ESP/BLE verification; this change neither merges that PR nor provides a second BLE implementation.
