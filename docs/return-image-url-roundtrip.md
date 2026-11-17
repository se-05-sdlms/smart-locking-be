# Return image URL investigation (#224)

The reported symptom is an absolute upload URL acquiring a second API origin in the return-create response. The current owning code has three relevant boundaries:

- `SupabaseImageStorageService` returns an absolute public object URL after a successful provider upload.
- `ImagesController` preserves absolute storage URLs. For root-relative storage paths it prepends the request scheme and host once. Both `/api/images` and `/api/uploads/images` reach that controller.
- `ReturnRequestService` trims the supplied `imageUrl` and persists it. Its create, detail and list mappings return that stored value without adding a host.

Mobile's return client passes the upload response's `url` directly into its create JSON and renders returned `imageUrl` directly. Shipper displays the pickup response's image URL directly. This source inspection does not establish what code or configuration produced the historical report.

`ReturnImageUrlHttpPostgresTests` exercises real Kestrel and MVC multipart upload, resident-authorized return creation, detail/list reads and persisted values in disposable migrated PostgreSQL databases. The absolute case uses the real Supabase storage service with a recording HTTP transport, a small PNG payload, an escaped folder and both upload aliases. The compatibility case replaces only storage with a root-relative path provider. Test identity replaces login; hardware/push/allocation and external providers are never called.

On dev baseline `c79fa3b`, the two HTTP/PostgreSQL tests passed with no skips. Neither absolute provider URLs nor root-relative compatibility paths acquired a duplicate host through upload, create, detail, list or persistence. This is a regression guard and a current-baseline non-reproduction; no production fix is claimed.

No production code, existing stored values, storage credentials or provider configuration have been changed. The original issue's `e2e-tests/evidence/round2/FE09/FE09-TC001.json` was not found in the available workspace, so historical deployment/client evidence still needs to be reconciled before closing the issue. Issue #224 remains open. These tests do not validate the external provider, real login, a deployed environment, native image rendering, signed URLs or historical malformed values already stored in a database.
