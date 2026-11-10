# Bluetooth recovery contract and deployment limits (#77, #198)

PR #166 reuses the existing wire protocol and MQTT topics from `smart-locking-esp32/docs/FIRMWARE_REBUILD.md`. This supplement merges current dev and separates credential reset from physical recovery confirmation.

## Recovery sequence

1. An authorized Operator/Admin requests transfer after physically handling the parcel. The API durably requests the existing z1 reset and reports Pending. No parcel removal, physical arrival notification or compartment release is inferred from dispatch.
2. A matching z2 OK receipt through the MQTT adapter consumes the old offline PIN and records ResetApplied. It updates the reported door state and removes the temporary reset block. It does not mark a parcel Removed, cancel a return or release its goods/reservations.
3. The Operator explicitly calls transfer again only after delivery to the existing Locker.RecoveryAddress. A confirmed reset is required. This call serializes with resident pickup and overdue processing using the #198 Parcel FOR UPDATE lock, rechecks eligibility/unresolved pickup, then atomically records RemovedAt/By, history, release, audit and arrival notification. Retrying this completed confirmation returns the same outcome.

Generic administrative reset only resets device credentials/state. Physical return cancellation/retrieval belongs to the authenticated resident workflow (#192); a reset ACK never supplies resident consent or physical retrieval proof.

Door ACK/status cannot establish delivery to RecoveryAddress or prove that a compartment contains no goods. Existing occupation records continue to block allocation until the appropriate physical-outcome workflow completes. New PostgreSQL race handling still needs deployment acceptance; no shared database migration or device operation was performed here.

## Trust boundary

The current c1/r1/o1/q1/z1/z2 firmware messages have structural validation but no message signature or MAC. An r1 string is not a cryptographically authenticated resident credential. A device's ability to verify a signed credential, revoke a previously cached grant, or attest installed firmware is not established by this BE change. MQTT acceptance relies on deployment TLS/broker identity/ACL protections; review those protections against real devices before activation.

HTTP relays of unsigned offline events/reset receipts remain rejected with bluetooth/unverified_evidence. Do not enable a client workaround that accepts a syntactically valid string as device proof. Keep Bluetooth policy disabled until deployment owners accept the existing device protocol and its limitations. Disabling central policy does not prove that a previously disconnected device has revoked its cached grants.

Resident return cancellation (#192) must therefore keep a clear pending result and preserve occupancy when offline/BLE revocation or prior physical pickup cannot be established. It must not claim safe cancellation/retrieval just because an API request, MQTT publish, door status or reset ACK succeeded.

## Validation limits

No tests/QA/fixtures are created or edited. The existing PR #166 tests include the older ACK-to-Removed/Cancelled expectations; those expectations conflict with the approved separation above and remain visible as failures rather than being silently rewritten. Firmware #150/#160, a real device, broker ACL verification and physical end-to-end acceptance remain external dependencies. #77 and #198 are not closed by this supplement.

Existing suite after this supplement: 145 total, 140 passed, 5 failed (Release). Unchanged failing expectations:

- `OverdueTransfer_WaitsForReceipt_ThenRevokesAndNotifiesOnlyOnce`: expects Removed at ACK; actual Overdue until Operator arrival confirmation.
- `LatePickupAfterTransfer_IsReviewedWithoutRevertingRemoved`: expects Removed after ACK; actual Overdue, event NeedsReview until Operator resolves recovery.
- `SimultaneousMqttAndBleReceipt_AppliesOnlyOnce`: expects a device reset to cancel a deposited return; it remains Deposited.
- `AssignedOperator_CanRequest_ButCannotCompleteBeforeReceipt`: same former reset-to-cancellation expectation.
- `OpenAsync_OperationalLocker_DispatchesAndRecordsSuccess`: fixture supplies no eligible persisted parcel for ResidentPickup; the direct unlock is rejected by the shared pickup revalidation guard.

Restore, full format verification and Release build pass (the existing IncidentService CS8602 warning remains). These failures are not waived; the PR remains draft and unmerged. Updating tests is outside the explicitly authorized scope.
