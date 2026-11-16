# Required return description (#223)

Creating a resident return requires a nonblank `note` alongside the existing required `imageUrl`. Null, empty and whitespace-only descriptions return HTTP 400 with `validation/invalid_input` and a `note` field error. Validation runs before the transaction, resident lookup, pickup-code generation, return persistence and audit creation.

Valid descriptions are trimmed at their edges; interior text, Vietnamese characters and line breaks are preserved. Create, detail and list responses expose the persisted description. No maximum length or new image URL policy is introduced by this change. Existing returns with a missing description remain readable and retain their lifecycle behavior; there is no backfill or migration.

The Mobile create form must label the description as required and validate before image upload or create submission, preserving photo/note on errors. Shipper only retrieves existing resident returns and does not create them, so it does not send this note field.

`ReturnNotePostgresTests` uses disposable migrated PostgreSQL databases. It verifies null/empty/Unicode whitespace rejection with no new return, audit or reservation; valid text round trips through create/detail/list with resident scope; and existing image validation. Allocation, hardware and push dependencies are absent to catch any unexpected external operation during metadata creation. The previous source reproduces one failing missing-note test and two passing preservation cases. No provider or shared application database is used.
