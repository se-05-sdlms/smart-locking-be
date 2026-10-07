# Locker Door Open/Close Flow — Design

## Intent

- Merge `IParcelService` and `IParcelPickupService` into one `IParcelService`.
- One shared "open door → wait for door-closed → finalize state" flow for locker access.
- One place for all MQTT traffic (BE → ESP32 commands, ESP32 → BE events).
- Door-closed is **sensor-driven**: ESP32 reports it over MQTT; no confirm APIs.

## Scope

In: MQTT gateway, device event handler, parcel service merge, Shipper drop-off flow, Resident pickup flow.
Out: Return flow (only a `switch` hook that logs "unsupported"), `DoorNotClosed` timeout worker, UI, ESP32 firmware, EMQX ACL config.

## MQTT contract

| Direction | Topic | Payload |
|---|---|---|
| BE → ESP32 | `boxora/lockers/{deviceId}/commands/unlock` | `{commandId, hardwareChannel}` |
| ESP32 → BE | `boxora/lockers/{deviceId}/events/command-ack` | `{commandId, ok}` |
| ESP32 → BE | `boxora/lockers/{deviceId}/events/door` | `{hardwareChannel, state: "open"|"closed", at}` |
| ESP32 → BE | `boxora/lockers/{deviceId}/events/status` | `{online}` (also LWT) |

- `commandId` == `LockerAccessEvent.Id`.
- One persistent MQTT connection (hosted service) replaces connect-per-command.
- Gateway only parses topic/payload and calls `ILockerDeviceEventHandler`; no business logic.

## Behaviour

- Every door event: write `LockerEvent(DoorChanged)`, update `LockerCompartment.DoorStatus`.
- `command-ack ok=false`: `LockerAccessEvent.Result = Failed`, `CompletedAt = now`.
- Door `closed` is honored only as a real **Open → Closed transition**: `LockerCompartment.DoorStatus` was `Open` before this event AND the event `at >= LockerAccessEvent.OccurredAt`. A `closed` report while the stored status is already `Closed`/`Unknown` (e.g. ESP32 reboot) never finalizes anything.
- When honored: find latest `LockerAccessEvent` of that compartment with `Result = Succeeded` and `CompletedAt == null`; switch on `AccessType`:
  - `ShipperDropOff` → create `Parcel` (Stored) + history, DeliveryRequest `Deposited`, release reservations.
  - `ResidentPickup` → Parcel `Retrieved` + history, release compartment.
  - `ResidentReturnDropOff` / `ShipperReturnPickup` → log unsupported.
  - Then set `CompletedAt`. Duplicate events find nothing pending → ignored (idempotent).
- Door-closed with no pending access: log only.
- Sensor wins over timers: `ExpireReservationsAsync` must skip any `Allocated` request that has a `LockerAccessEvent` with `Result = Succeeded` and `CompletedAt == null` (door opened, shipper still placing the parcel), so the reservation is not released and the request not expired mid drop-off. A drop-off closed after `ReservationExpiresAt` is still finalized (parcel physically inside).

## API changes

- Remove `POST /api/delivery-requests/{id}/reserve-compartment`, `POST /api/delivery-requests/{id}/confirm-drop-off` (**breaking for shipper clients**).
- Add `POST /api/delivery-requests/{id}/open-compartment` (guest token): Approved → pick one free compartment with a single query, reserve, `Allocated`, open (`ShipperDropOff`, `GuestSession`). Already `Allocated` with valid reservation → re-open same compartment (retry).
- Keep `POST /api/parcels/{id}/unlock-pickup`; remove `ConfirmPickupAsync`.
- `ParcelsController` injects only `IParcelService`.

## Data

- `LockerAccessEvent.CompletedAt` (`DateTimeOffset?`) — null = opened, not yet confirmed closed. One migration.
