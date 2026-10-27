# BLE / resident return integration review

The offline journal now serializes known returns with cancellation using the same Return FOR UPDATE lock. A previously unknown unsigned offline R event is preserved as NeedsReview and opens a compartment incident; it cannot manufacture a resident obligation. Known physical deposit/pickup events still reconcile, but a pending cancellation never republishes its old pickup code. An actual collected return resolves that request as AlreadyCollected.

Provisioning and inventory use the shared operational-compartment gates, including maintenance and unresolved compartment incidents. Expired parcel eligibility no longer publishes a fresh PIN. Existing occupied returns and pending cancellations remain busy. A journal closure updates the observed door state only when its device timestamp is newer than the stored observation; absent device time cannot masquerade as a fresh closed-door observation.

Return cancellation and guest access share ReturnTransitionLock; parcel access/recovery share ParcelTransitionLock. Allocation subsequently locks the Locker row. Maintenance locks the Locker row without acquiring a Return row, so these reviewed paths introduce no reverse lock order. Existing reset ACK processing remains credential-only and supplies no arrival, empty-compartment or resident-retrieval proof.

An unconsumed offline PIN can outlive a central terminal status. Account/locker changes must check unconsumed PINs for the resident without filtering terminal parcel status, plus door access without a later observed closure. This change does not attest firmware, establish signed offline credentials or prove revocation on a disconnected device. Resident retrieval after cancellation remains unavailable pending authoritative evidence.

## Existing checks

Selected-file format and Release build pass. Existing suite: 145 total, 137 passed, 8 failed. No tests, fixtures or QA scripts were edited. The local TRX is `%TEMP%/boxora-integration-checks/ble-return-integration.trx`.

- `CompartmentResetTests.OverdueTransfer_WaitsForReceipt_ThenRevokesAndNotifiesOnlyOnce` and `LatePickupAfterTransfer_IsReviewedWithoutRevertingRemoved` expect Removed at reset ACK; approved recovery requires explicit Operator arrival confirmation.
- `CompartmentResetTests.SimultaneousMqttAndBleReceipt_AppliesOnlyOnce` expects reset ACK to cancel a deposited return; approved resident cancellation preserves that obligation.
- `CompartmentResetTests.AssignedOperator_CanRequest_ButCannotCompleteBeforeReceipt` and `LockerAccessServiceTests.UpdateLockerStatus_EnforcesScopeAndRecordsActor` (LockerOperator, assigned, not revoked, allowed) omit the active Operator user required by effective coverage authorization.
- `LockerAccessServiceTests.OpenAsync_OperationalLocker_DispatchesAndRecordsSuccess` and `OpenAsync_OfflineLockerWithMonitoring_AllowsBluetoothWithoutMqttDispatch` omit an eligible persisted parcel/return now required by access revalidation.
- `MqttLockerListenerTests.MqttJournal_AppliesOrderedReturnCycle_AndIgnoresDuplicate` expects creation of an unknown return from unsigned offline R evidence; the approved boundary records NeedsReview and a blocking incident instead.

These failures remain visible under the no-test-edit constraint. No shared database migration or hardware operation was performed. Device acceptance, firmware trust and broker identity/ACL verification remain deployment dependencies; related PRs remain draft.
