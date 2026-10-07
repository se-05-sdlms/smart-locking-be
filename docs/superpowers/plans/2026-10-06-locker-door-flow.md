# Locker Door Open/Close Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One sensor-driven open→close flow for locker compartments, one MQTT gateway, and a single `IParcelService`.

**Architecture:** `LockerAccessService.OpenAsync` stays the only "open door" entry. A persistent `LockerMqttGateway` publishes unlock commands and forwards ESP32 events to `ILockerDeviceEventHandler`, which finalizes the business state (drop-off / pickup) when the door closes, keyed by `LockerAccessEvent.CompletedAt == null`.

**Tech Stack:** .NET, EF Core, MQTTnet (already referenced), PostgreSQL migrations.

**Spec:** `docs/superpowers/specs/2026-10-06-locker-door-flow-design.md`

## Execution status (2026-10-06)

Implemented all six tasks. Solution build succeeds with 0 warnings and 0 errors. No tests written or run per user request; obsolete test references were adjusted only for compilation. Migration generated but not applied. Changes remain uncommitted; breaking changes documented in both READMEs, without sending external team messages.

Review corrections: indeterminate MQTT publication keeps access pending; opening and expiry coordinate using a delivery-request row lock. Allocation excludes overdue parcels and unreleased reservations. No extra filtered index was added because the existing access configuration does not use HasFilter.

## Global Constraints

- MQTT topics exactly as in spec: `boxora/lockers/{deviceId}/commands/unlock`, `.../events/command-ack`, `.../events/door`, `.../events/status`.
- `commandId` == `LockerAccessEvent.Id`; QoS `AtLeastOnce`.
- No new tables; one new nullable column `LockerAccessEvent.CompletedAt`.
- Return-flow `AccessType`s only log "unsupported".
- Finalize methods mutate tracked entities and do NOT call `SaveChangesAsync`; the event handler commits once (atomic with `CompletedAt`).
- Tests are out of scope per request, except removing/adjusting existing tests that no longer compile.

## Review Focus

- Duplicate `door closed` event → must not create a second `Parcel`.
- `closed` reported while `DoorStatus` is already `Closed`/`Unknown` (ESP32 reboot) → must not finalize.
- Reservation expiry timer fires while shipper's door is open and unconfirmed → request must not be expired.
- `door closed` with no pending access → ignored, only logged.
- Malformed MQTT payload / unknown topic → logged and skipped, connection survives.
- `command-ack ok=false` → `Result = Failed`, reservation kept so shipper can retry `open-compartment`.
- MQTT host not configured → gateway idles, dispatch throws `InvalidOperationException` (as today).

---

### Task 1: `CompletedAt` column

**Files:**
- Modify: `smart-locking-be.Domain/Entities/LockerAccessEvent.cs` (add after `OccurredAt`)
- Modify: `smart-locking-be.Infrastructure/Persistence/Configurations/LockerAccessEventConfiguration.cs`
- Create: migration `AddLockerAccessEventCompletedAt`

**Interfaces:**
- Produces: `LockerAccessEvent.CompletedAt : DateTimeOffset?` (XML doc in Vietnamese, same style as siblings).

- [x] **Step 1:** Add `public DateTimeOffset? CompletedAt { get; set; }` with Vietnamese summary ("Thời điểm cửa ngăn được xác nhận đóng hoặc lệnh mở thất bại; null khi còn chờ xác nhận.").
- [x] **Step 2:** Add a filtered index in the configuration on `(LockerCompartmentId, OccurredAt)` where `CompletedAt IS NULL` only if the provider config pattern in this file already uses `HasFilter`; otherwise skip the index.
- [x] **Step 3:** Run `dotnet ef migrations add AddLockerAccessEventCompletedAt --project smart-locking-be.Infrastructure --startup-project smart-locking-be.API`.
- [x] **Step 4:** Run `dotnet build` — Expected: success.
- [ ] **Step 5:** Commit `feat: add LockerAccessEvent.CompletedAt`.

---

### Task 2: Merge parcel services

**Files:**
- Modify: `Application/Interfaces/Services/IParcelService.cs`, `Infrastructure/Services/ParcelService.cs`
- Delete: `Application/Interfaces/Services/IParcelPickupService.cs`, `Infrastructure/Services/ParcelPickupService.cs`, `PickupConfirmationResponse` record
- Modify: `API/Controllers/ParcelsController.cs`, `Infrastructure/DependencyInjection.cs:40`

**Interfaces:**
- Produces on `IParcelService`:
  - `Task<PickupUnlockResponse> UnlockPickupAsync(Guid residentUserId, Guid parcelId, string? ipAddress, string? deviceContext, CancellationToken cancellationToken = default)` (body moved from `ParcelPickupService.UnlockAsync`, unchanged logic)
  - `Task FinalizeRetrievalAsync(Guid parcelId, Guid? userId, DateTimeOffset at, CancellationToken cancellationToken = default)` — Parcel `Retrieved`, `RetrievedAt`, `UpdatedAt`, `DeliveryRequest.CompartmentReleasedAt`, adds `ParcelStatusHistory` (reason text kept from old `ConfirmPickupAsync`). Does not save. Already `Retrieved` → return silently.
- `ParcelService` ctor adds `ILockerAccessService lockerAccessService`.

- [x] **Step 1:** Add the two methods to `IParcelService`; move/adapt bodies into `ParcelService`.
- [x] **Step 2:** Delete `IParcelPickupService`, `ParcelPickupService`, `PickupConfirmationResponse`; remove its DI line.
- [x] **Step 3:** `ParcelsController`: ctor takes only `IParcelService`; `UnlockPickup` calls `parcelService.UnlockPickupAsync`.
- [x] **Step 4:** Run `dotnet build` — Expected: errors only from old tests (handled in Task 6); production projects compile.
- [ ] **Step 5:** Commit `refactor: merge IParcelPickupService into IParcelService`.

---

### Task 3: Shipper drop-off via `open-compartment`

**Files:**
- Modify: `Application/Interfaces/Services/IDeliveryRequestService.cs`, `Infrastructure/Services/DeliveryRequestService.cs`, `API/Controllers/DeliveryRequestsController.cs`
- Modify: `Application/DTOs/DeliveryRequests/*` (replace `CompartmentReservationResponse`, delete `DropOffConfirmationResponse`)

**Interfaces:**
- Produces:
  - `Task<OpenCompartmentResponse> OpenCompartmentAsync(Guid requestId, string guestSessionToken, string? ipAddress, string? deviceContext, CancellationToken ct = default)`
  - `Task FinalizeDropOffAsync(Guid deliveryRequestId, DateTimeOffset at, CancellationToken ct = default)` — body of old `ConfirmDropOffAsync` minus token check and minus reservation-expiry check, creates `Parcel` + `ParcelStatusHistory`, releases reservations, `Deposited`; no save; already `Deposited` → return silently.
  - `record OpenCompartmentResponse(Guid RequestId, Guid CompartmentId, string CompartmentCode, Guid AccessEventId, LockerAccessResult Result, string? FailureReason, DateTimeOffset ReservationExpiresAt)`
- Consumes: `ILockerAccessService.OpenAsync` (inject into `DeliveryRequestService`).

- [x] **Step 1:** Implement `OpenCompartmentAsync`: `FindValidSessionAsync`; status `Approved` → select first compartment by `Code` with one LINQ query (Operational, no `Stored` parcel, no active reservation), none → existing `NoCompartment` failure path; create `CompartmentReservation`, set `Allocated`, save; status `Allocated` with `ReservationExpiresAt > now` → reuse `AllocatedCompartmentId`; any other status → `InvalidOperationException`. Then call `OpenAsync(new OpenLockerRequest(..., AccessType: ShipperDropOff, AccessMethod: GuestSession, UserId: null, DeliveryRequestId: id, ...))`.
- [x] **Step 2:** Implement `FinalizeDropOffAsync`; delete `ReserveCompartmentAsync` and `ConfirmDropOffAsync` from interface and class.
- [x] **Step 2b:** In `ExpireReservationsAsync` add to the `Where`: `!dbContext.LockerAccessEvents.Any(e => e.DeliveryRequestId == r.Id && e.Result == LockerAccessResult.Succeeded && e.CompletedAt == null)`, so a request whose door is open and unconfirmed is not expired or its reservation released.
- [x] **Step 3:** Controller: replace `reserve-compartment` and `confirm-drop-off` with `[HttpPost("{id:guid}/open-compartment")] [AllowAnonymous]`; map `Result` like `ParcelsController.UnlockPickup` (`Succeeded→202`, `Blocked→409`, else `503`); pass `RemoteIpAddress` and `User-Agent`.
- [x] **Step 4:** Run `dotnet build` — Expected: production projects compile.
- [ ] **Step 5:** Commit `feat: shipper drop-off opens compartment via open-compartment`.

---

### Task 4: MQTT gateway

**Files:**
- Create: `Infrastructure/Services/LockerMqttGateway.cs`, `Application/Interfaces/Services/ILockerDeviceEventHandler.cs`, `Application/DTOs/Lockers/LockerDeviceEventDtos.cs`
- Delete: `Infrastructure/Services/MqttLockerCommandDispatcher.cs`
- Modify: `Infrastructure/DependencyInjection.cs:37`

**Interfaces:**
- Produces:
  - `ILockerDeviceEventHandler`: `Task HandleCommandAckAsync(string deviceId, Guid commandId, bool ok, CancellationToken ct)`, `Task HandleDoorChangedAsync(string deviceId, int hardwareChannel, DoorStatus state, DateTimeOffset at, CancellationToken ct)`, `Task HandleConnectionChangedAsync(string deviceId, bool online, CancellationToken ct)`.
  - Payload records: `CommandAckPayload(Guid CommandId, bool Ok)`, `DoorEventPayload(int HardwareChannel, string State, DateTimeOffset? At)`, `StatusPayload(bool Online)`.
  - `LockerMqttGateway : BackgroundService, ILockerCommandDispatcher` (singleton).

- [x] **Step 1:** Create handler interface and payload records.
- [x] **Step 2:** `LockerMqttGateway`: reads same `Mqtt:*` config keys as the old dispatcher; if `Host` empty, log warning and idle. `ExecuteAsync`: loop — connect, subscribe `boxora/lockers/+/events/#` at QoS 1, wait until disconnected, delay 5 s, retry. Incoming message: split topic, deserialize with `System.Text.Json` (case-insensitive), open `IServiceScopeFactory` scope, call the matching `ILockerDeviceEventHandler` method; catch/log all exceptions per message.
- [x] **Step 3:** `DispatchUnlockAsync`: publish on `boxora/lockers/{DeviceIdentifier}/commands/unlock` with `{commandId, hardwareChannel}` using the shared client; not connected → `InvalidOperationException("MQTT broker chưa kết nối.")`.
- [x] **Step 4:** DI: `AddSingleton<LockerMqttGateway>()`, `AddSingleton<ILockerCommandDispatcher>(sp => sp.GetRequiredService<LockerMqttGateway>())`, `AddHostedService(sp => sp.GetRequiredService<LockerMqttGateway>())`. Delete old dispatcher.
- [x] **Step 5:** Run `dotnet build` — Expected: fails only on missing `ILockerDeviceEventHandler` implementation registration (resolved in Task 5).
- [ ] **Step 6:** Commit `feat: persistent MQTT gateway for locker commands and events`.

---

### Task 5: Device event handler

**Files:**
- Create: `Infrastructure/Services/LockerDeviceEventHandler.cs`
- Modify: `Infrastructure/DependencyInjection.cs` (`AddScoped<ILockerDeviceEventHandler, LockerDeviceEventHandler>`)

**Interfaces:**
- Consumes: `IParcelService.FinalizeRetrievalAsync` (Task 2), `IDeliveryRequestService.FinalizeDropOffAsync` (Task 3), `ILockerDeviceEventHandler` (Task 4), `LockerAccessEvent.CompletedAt` (Task 1).
- Ctor: `ApplicationDbContext`, `IParcelService`, `IDeliveryRequestService`, `TimeProvider`, `ILogger<LockerDeviceEventHandler>`.

- [x] **Step 1:** `HandleCommandAckAsync`: load `LockerAccessEvent` by id; not found or `CompletedAt != null` → return. `ok=false` → `Result = Failed`, `FailureReason = "Thiết bị từ chối lệnh mở ngăn."`, `CompletedAt = now`; save.
- [x] **Step 2:** `HandleDoorChangedAsync`: resolve compartment by `Locker.DeviceIdentifier == deviceId && HardwareChannel == hardwareChannel`; unknown → log, return. Keep `DoorStatus previous = compartment.DoorStatus`, then update `DoorStatus`, add `LockerEvent(DoorChanged, Previous/NewValue, Severity Info, OccurredAt = at, ReceivedAt = now)`.
- [x] **Step 3:** If `state == Closed && previous == DoorStatus.Open` (real transition; otherwise save the event log and return): load latest `LockerAccessEvent` for the compartment with `Result == Succeeded && CompletedAt == null && OccurredAt <= at`; none → save event log, return. Else `switch (AccessType)`: `ShipperDropOff` → `FinalizeDropOffAsync(DeliveryRequestId!.Value, at)`; `ResidentPickup` → `FinalizeRetrievalAsync(ParcelId!.Value, UserId, at)`; return types → log "chưa hỗ trợ" and leave `CompletedAt` null. Set `CompletedAt = at` for handled types; single `SaveChangesAsync`.
- [x] **Step 4:** `HandleConnectionChangedAsync`: update `Locker.ConnectionStatus` (`Online`/`Offline`), `LastSeenAt = now` when online; add `LockerEvent(ConnectionChanged)`; save.
- [x] **Step 5:** Register in DI; run `dotnet build` — Expected: production projects compile with no errors.
- [ ] **Step 6:** Commit `feat: finalize drop-off and pickup on door-closed events`.

---

### Task 6: Cleanup and docs

**Files:**
- Modify/Delete: `smart-locking-be.Tests/Parcels/ParcelPickupServiceTests.cs`, `Tests/DeliveryRequests/DeliveryRequestServiceTests.cs`, `Tests/Lockers/LockerAccessServiceTests.cs` — only the parts referencing removed APIs (`IParcelPickupService`, `ConfirmPickupAsync`, `ReserveCompartmentAsync`, `ConfirmDropOffAsync`, `MqttLockerCommandDispatcher`) so the solution compiles.
- Modify: `README.md` / `README.vn.md` — add the MQTT contract table from the spec.

- [x] **Step 1:** Remove or rewrite only the compile-breaking test members.
- [x] **Step 2:** Add the MQTT contract section to both READMEs.
- [x] **Step 3:** Run `dotnet build` — Expected: whole solution builds, 0 errors.
- [ ] **Step 4:** Commit `chore: remove obsolete tests, document MQTT contract`.
- [ ] **Step 5:** Notify the shipper client team of the breaking change (`reserve-compartment` and `confirm-drop-off` removed, `open-compartment` added) and the ESP32 owner of the MQTT contract.
