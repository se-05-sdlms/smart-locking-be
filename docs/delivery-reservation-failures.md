# Delivery allocation and failed opens (#228)

`POST /api/delivery-requests/{id}:openCompartment` serializes allocation with a
PostgreSQL lock on that delivery request. The allocation transaction commits the
reservation, delivery status and allocation audit together, then releases its
locks before calling the device transport. A concurrent call for that delivery
sees `Allocated` and receives the existing 409 `delivery/invalid_state` contract;
it cannot allocate a second compartment or publish another OPEN.

When `LockerAccessService` rejects an open with the known pre-dispatch offline
or invalid-compartment guards, the caller releases only the reservation created
by that operation and durably records `Failed` / `DeviceUnavailable` with a failed
audit before rethrowing the original API error. Existing blocked/failed response
handling uses the same exact-reservation cleanup. Other reservations, including
another reservation with the same delivery owner, are preserved. Historical
allocation IDs/timestamps remain for investigation; `ReleasedAt` ends the hold.

A successful open keeps the reservation until the normal confirmation/expiry
flow. The existing failed-dispatch response still produces 503 and releases its
reservation. Unknown exceptions or cancellation do not trigger broad cleanup:
the OPEN may have reached the device, so the already committed allocation is
retained for the existing expiry/recovery flow. Publishing and SQL persistence
are not atomic and this change does not prove physical door state.

Eight regression cases use real service code and migrated, disposable PostgreSQL
databases: offline cleanup/reuse, same-request overlap, preservation of another
owner and another reservation of the same owner, failed-dispatch response,
post-publish SQL failure, invalid session and no available compartments. Device
transport is a controlled fake; no shared application database or physical
locker is modified.
