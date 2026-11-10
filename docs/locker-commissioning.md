# Locker commissioning software (#183)

New lockers created through `POST /api/lockers` start `OutOfService` with persisted commissioning stage `Declared`. Existing lockers have no commissioning row and preserve their operational status; GET identifies them with `isLegacyLocker: true`. This migration does not claim that old devices have passed new commissioning checks.

All commissioning routes are Administrator-only under `/api/admin/lockers/{lockerId}/commissioning`. An installer must use an authorized Admin account; no public route exposes credentials, firmware configuration or handover controls. Existing public locker QR remains unchanged. Never paste Wi-Fi/MQTT secrets into notes or release URLs.

| Method/path suffix | Body and result |
| --- | --- |
| GET | Current stage, revision, device identifiers, installer-confirmed firmware, release manifest, blockers and audit history |
| POST `bind-device` | `{ expectedRevision, deviceIdentifier, notes }`; confirm the existing exact device identity, enter `AwaitingDevice` |
| POST `configure` | `{ expectedRevision, firmwareVersion, hardwareModel, firmwareSha256, installationAndNetworkConfirmed, notes }`; enter `Configuring` after an online signal received since binding |
| POST `verify` | `{ expectedRevision, everyDoorPhysicallyTested, wiringAndRelaySafetyConfirmed, notes }`; enter `Verified` if telemetry/configuration checks pass |
| POST `handover` | `{ expectedRevision, acceptedForOperation, notes }`; record actor/time, enter `HandedOver` and activate the locker atomically |

Every mutation requires the current `revision`. A stale client receives HTTP 409 `locker/not_operational` and must GET the current progress. Steps persist across restarts. Re-running configure from `Configuring` or `Verified` begins a fresh verification window and clears the earlier verification. Audit records preserve previous attempts. The Installer confirms the physical checks; the software does not assert firmware attestation or infer installation from an ACK.

## Deployment release configuration

Provide a real, compatible, prebuilt device release through deployment configuration. No default/download placeholder is shipped, and an absent/invalid release prevents configuration and handover.

```text
LockerCommissioning__FirmwareVersion=<actual published version>
LockerCommissioning__HardwareModel=<supported hardware model>
LockerCommissioning__DownloadUrl=<HTTPS URL of the actual firmware package>
LockerCommissioning__Sha256=<64 hexadecimal characters for the package>
LockerCommissioning__InstallationGuideUrl=<HTTPS URL of the installer instructions>
LockerCommissioning__ObservationFreshnessSeconds=<deployment-approved value from 30 through 3600>
```

URLs must contain no user credentials, query parameters or fragments. They are presented as supplied to Admins; the API does not download arbitrary URLs. The release owner must publish the real package and instructions for the target board/network/relay wiring. Their existence and compatibility must be verified by deployment/device owners. This implementation does not produce firmware, change #150/#160, provision credentials or flash devices.

The supplied installation guide must let the installer identify the board/device, compare the downloaded checksum/model/version, safely flash the prebuilt package with its supplied tool, enter Wi-Fi/MQTT configuration through the firmware-supported provisioning method, confirm routing/identity, test each physical door/relay and recover safely from interruption. If the actual firmware has no such provisioning method or package, leave the corresponding step blocked; do not ask the installer to build source as a substitute.

## Verification and handover gates

Configure requires the bound device to report `Online` with a fresh `LastSeenAt` received since binding. Verify and handover both recheck the server's release metadata, device identity, fresh heartbeat and all configured compartments. Each compartment must have a supported hardware channel (1..4), remain operational, have a closed door and a fresh door observation at or after the configure step. Existing MQTT observations are reused; no device ACK is fabricated. The installer separately confirms every physical door and relay/wiring safety. Open maintenance or unresolved incidents block handover.

A hash of device identity, addresses, compartment identity/hardware mapping and confirmed firmware detects changes after verification. If it changes, configure and verify again. Parent locker row locks serialize handover with generic locker/compartment edits. Generic Operational status updates cannot activate an unfinished commissioning record, and a bound device cannot be silently replaced by profile edit. Adding/changing hardware mapping after handover requires a separate recommissioning procedure and is blocked for these newly commissioned lockers; old locker behavior is preserved.

No shared/production database migration or hardware operation was performed. PostgreSQL concurrency, actual release installation, MQTT freshness and a physical end-to-end handover remain deployment acceptance work. #183 stays open until a compatible package/guide and real device checks are available; this PR delivers the independent BE software workflow only. FE #81 remains owned by the FE team.
