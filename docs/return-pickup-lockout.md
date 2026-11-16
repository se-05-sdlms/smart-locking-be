# Return pickup verification throttle (#226)

`POST /api/return-pickup-sessions` permits four failed code verifications per
locker/client-address bucket. The fifth failure returns HTTP 429,
`return/pickup_locked`, with a `Retry-After` header. Further attempts, including a
correct code, remain blocked until the fixed 15-minute window expires. Blocked
requests do not extend that window. Invalid codes and codes for used/not-ready
returns count as failures. Successful verification resets the bucket. Malformed
codes are rejected before lookup; unknown lockers do not allocate throttle rows.

Buckets use the locker ID and SHA-256 of the normalized client IP. IPv4-mapped
IPv6 addresses share the IPv4 bucket. Raw IP addresses and pickup codes are not
stored in throttle rows or added to logs. Separate IPs and lockers have independent
buckets, so one client cannot lock every user out of a locker. Clients sharing a
NAT/proxy address share that address's bucket; the existing general request limiter
still applies.

The controller supplies `Connection.RemoteIpAddress`. Missing or invalid addresses
fail closed with HTTP 503 and `return/pickup_verification_unavailable`. It does not
trust caller-provided `X-Forwarded-For`. Deployments behind a reverse proxy must
confirm the actual trusted-proxy/forwarded-header configuration before relying on
individual end-client isolation. This change does not invent a proxy trust list.

PostgreSQL upsert and row locking serialize requests across service instances.
Failed attempts are saved and committed before returning the rejection. Successful
verification commits the new session token and counter reset together. The service
owns the transaction for ordinary HTTP calls; callers supplying an outer transaction
must commit rejected attempts themselves. Verification does not open a compartment.
Expired rows are cleaned before the verification transaction in batches of at most
100 with `SKIP LOCKED` so cleanup
does not wait for unrelated active verification buckets. The expiry index supports
bounded cleanup. No shared database migration is applied by this change.

## Validation

`ReturnPickupPostgresTests` migrates one random disposable database per test and
drops it afterward. Set `SMART_LOCKING_TEST_POSTGRES` to an isolated PostgreSQL
administrator connection with permission to create databases; fixtures connect to
`postgres` for administration, never the configured application database. CI runs
PostgreSQL 17. Without that variable, these integration tests explicitly skip.

The eight PostgreSQL scenarios cover durable counters, 20 concurrent failures,
correct-code rejection at lockout, fixed expiry, success/reset without physical
access, IP/locker isolation and mapped-address normalization, unavailable client IP,
unknown locker allocation, concurrent cleanup of expired buckets, and used-code lockout. An API exception-handler test
checks HTTP 429, stable problem code and numeric `Retry-After`.

The demo seeder intentionally leaves transient security buckets empty; it must not
pre-lock clients through demo data.
