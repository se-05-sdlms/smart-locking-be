# DeliveryRequestService unit tests — issue #117

Source matrix: `REVIEW2/Report5_Unit Test_working.xlsx`, sheets `Delivery-Create` through `Delivery-FinalizeDropoff`. The workbook is external to this repository. The issue's generated list says 11 UTCs and `undefined`; the actual matrix defines **61 cases across 11 functions**.

Each matrix column `UTCIDxx` maps to a separate xUnit `[Fact]` named `<MethodName>_UTCxx`, with `[Trait("Category", "DEL-xx")]`. Helpers share setup and assertions for symmetric approve/reject and expiration workflows; every UTC remains independently discoverable.

## Case mapping

| Category / sheet | Method | UTC mapping in column order |
| --- | --- | --- |
| DEL-01 / Delivery-Create | CreateAsync | 01 create session, token hash, policy and audit; 02 missing locker; 03 unavailable locker; 04 no active policy; 05 null request; 06 empty code; 07 code length 51 |
| DEL-02 / Delivery-Submit | SubmitAsync | 01 persist recipient/image/approval snapshot and audit; 02 no eligible resident; 03 different registered locker; 04 empty ID/token and null request; 05 missing token; 06 relative image URL; 07 auto approval without push; 08 manual approval with deadline and enqueue/send |
| DEL-03 / Delivery-Get | GetAsync | 01 current status; 02 missing request; 03 empty ID/token; 04 approval expires exactly now and is saved; 05 compartment code and parcel ID |
| DEL-04 / Delivery-ExpireStarted | ExpireStartedSessionsAsync | 01 elapsed sessions; 02 no eligible sessions returns zero; 03 deadline equals now |
| DEL-05 / Delivery-GetPending | GetPendingRequestsForResidentAsync | 01 resident/status/deadline filtering, descending ordering, page 10 of size 10, policy fallback; 02 no active resident; 03 empty resident ID and zero paging boundaries |
| DEL-06 / Delivery-Approve | ApproveDeliveryRequestAsync | 01 successful approval; 02 missing request; 03 wrong owner; 04 wrong status; 05 expired deadline; 06 empty IDs; 07 null approval deadline |
| DEL-07 / Delivery-Reject | RejectDeliveryRequestAsync | 01 successful rejection; 02 missing request; 03 wrong owner; 04 wrong status; 05 expired deadline; 06 empty IDs; 07 null approval deadline |
| DEL-08 / Delivery-ExpireApproval | ExpirePendingApprovalsAsync | 01 elapsed approvals; 02 no eligible approvals returns zero; 03 deadline equals now |
| DEL-09 / Delivery-OpenCompartment | OpenCompartmentAsync | 01 reservation/open command and allocation audit; 02 wrong status; 03 no available compartment; 04 blocked device with fallback message; 05 empty ID/token; 06 failed device with explicit reason; 07 failed device without reason |
| DEL-10 / Delivery-ExpireReserve | ExpireReservationsAsync | 01 elapsed reservations and release; 02 no eligible reservations returns zero; 03 deadline equals now |
| DEL-11 / Delivery-FinalizeDropoff | FinalizeDropOffAsync | 01 persist parcel/history, deadlines, release, notification and audit using event time; 02 missing request; 03 wrong status; 04 missing compartment; 05 expired reservation; 06 empty ID/default completion; 07 repeated completion has no additional effects; 08 locker realtime event |

Counts in category order: **7, 8, 5, 3, 3, 7, 7, 3, 7, 3, 8 = 61**.

## Matrix ambiguities and current behavior

These tests characterize the existing service; they do not change production behavior to match ambiguous template cells.

- **DEL-01 UTC05:** null request has no specified exception in the matrix. The service dereferences it and throws `NullReferenceException`; assert that no request, audit or external call is created.
- **DEL-02 UTC04, DEL-03 UTC03, DEL-09 UTC05:** empty guest token is rejected before ID lookup or request validation with `AuthenticationException` / `Thiếu X-Guest-Session-Token.`. DEL-03 UTC03's successful-response confirmation conflicts with this authentication contract.
- **DEL-04/08/10 UTC02:** `KeyNotFoundException / Empty` is a template placeholder. Expiration methods return `0` when nothing qualifies. Seed future and wrong-status records to prove they are untouched. Normal and boundary expiration cases also include future, wrong-status and (where nullable) missing-deadline controls.
- **DEL-05 UTC03:** an empty resident ID without an active profile throws `KeyNotFoundException` before pagination. The same UTC also checks zero page/size with a valid resident and expects normalization to page 1 / size 1. This isolates both boundary behaviors rather than expecting a successful response for a nonexistent resident.
- **DEL-06/07 UTC06:** empty user/request IDs fail active-profile lookup first, with `Không tìm thấy hồ sơ Cư dân hợp lệ.`. Missing-request UTC02 seeds a valid resident so it reaches the intended request lookup.
- **DEL-09 UTC04:** use a blocked locker response with no failure reason to exercise `Không thể mở ngăn locker.`. UTC06 uses `Failed` / `Device offline`; UTC07 uses `Failed` / null. The service saves allocation and its successful audit **before** opening the device, then releases the reservation and saves `Failed` / `DeviceUnavailable` on device failure. The matrix's UTC04 `Log message=null` does not match that persisted allocation audit; the tests explicitly verify the existing audit.
- **DEL-11 UTC06:** `completedAt=null` cannot be passed to non-nullable `DateTimeOffset`. Use `default(DateTimeOffset)` together with `Guid.Empty`; the request lookup throws before the timestamp is used. This does not establish a null-time validation contract.

## Isolation and assertions

- Fresh EF Core InMemory database per test; no production database, network, MQTT or push gateway.
- Real SHA-256 token hashing; recording fakes for push, allocation, locker access and realtime. Enqueue/send arguments and call counts are tested at the service boundary; notification persistence and actual hardware behavior belong to those dependencies' own tests.
- Fixed `TimeProvider` for exact deadline boundaries. Completion additionally verifies that the supplied event timestamp, rather than the clock, controls persisted completion time.
- Persistence checks clear EF's change tracker before reading saved state. Assertions cover response fields, exception type/message, state transitions, audit identity/action/actor/result/time, released allocation and absence of unintended effects. Expiration tests verify batch counts and unaffected records; repeated completion verifies unchanged parcel/history/audit identities and no new external calls.
- InMemory does not verify relational constraints, transactions or production SQL translation.

## Validation

Run from the repository root:

```powershell
dotnet restore smart-locking-be.sln
dotnet format smart-locking-be.sln --verify-no-changes --no-restore
dotnet build smart-locking-be.sln --configuration Release --no-restore
dotnet test smart-locking-be.sln --configuration Release --no-build --filter FullyQualifiedName~DeliveryRequestServiceTests
dotnet test smart-locking-be.sln --configuration Release --no-build
```

Category filtering is also supported, for example `--filter "Category=DEL-09"`.

Verified on 2026-10-14: Delivery-only Debug run **61 passed, 0 failed, 0 skipped**; full Release suite **114 passed, 0 failed, 0 skipped** (53 Auth + 61 Delivery); Release build **0 warnings, 0 errors**; solution-wide format verification passed. Matrix reconciliation also verified all 61 unique method names and category traits against the workbook's UTC columns.
