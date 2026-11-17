# Audit and operational phone privacy (#225)

The audit and operational history APIs mask Vietnamese phone tokens when creating responses. For example, `0901234001`, `+84901234001`, `84901234001`, and `090 123 4001` become `090****001`. Dots and hyphens between digits are also recognized. The local first three and last three digits remain for identifying a record without displaying the full number.

## Covered responses

- `GET /api/audit-logs` and `GET /api/audit-logs/{id}`: actor phone fallback and historical free-text details.
- `GET /api/operational-records`: delivery recipient phone and free-text summaries, including incident titles, maintenance descriptions, locker-event details/reasons, emergency reasons, and locker addresses. Compartment hardware identifiers are preserved.
- `GET /api/operational-records/{kind}/{id}`: recipient phone and free-text title, description, resolution, removal reason, previous/new value, reason, and details fields.

The response transformation runs after filtering and authorization. Existing record IDs, codes, timestamps, status values, IP addresses, email addresses, detail keys, total count and pagination are preserved. Queries still match the stored phone text; matching does not return the full phone. Unauthorized locker errors do not echo the query. The FE admin audit table and operator history views receive masked values without a schema change.

## Access and historical evidence

Audit routes remain Administrator-only. Operational routes remain Administrator/LockerOperator-only. The service uses effective operator assignments, including active account/assignment timing, when listing or returning details; an out-of-scope detail remains 404 and an explicitly requested out-of-scope locker remains 403.

Masking applies equally to newly written and existing records. Stored audit details and delivery snapshots are not modified, deleted or re-saved by reads. No migration, backfill or retention policy is introduced.

## Recognition boundary

The helper recognizes standalone 10-digit local numbers beginning `0` and a nonzero digit, or equivalent 9-digit subscriber numbers after `84`/`+84`. It permits one space, dot or hyphen between digits and after the country code. Already masked text stays unchanged. It avoids matching inside letters, longer numbers, email addresses, underscore/hyphen identifiers, and UUIDs. Structured identity/code/time fields bypass masking. A standalone number in free text with the same shape as a phone is masked conservatively.

This change covers phone exposure on these history endpoints. It does not define a new email/IP redaction policy, redact arbitrary international numbers, or change role-specific contact fields on other workflow APIs.

## Verification

`OperationalPhonePrivacyPostgresTests` creates a disposable migrated PostgreSQL database and exercises historical audit list/detail, raw-phone search, delivery list/detail for both authorized roles, free-text event/incident/address output, unchanged stored evidence, and denied/revoked/disabled/future-assignment access. `OperationalPhoneMaskTests` covers normalization, separators, JSON, Unicode surrounding text, idempotency, and identifier/date/email/IP boundaries.
