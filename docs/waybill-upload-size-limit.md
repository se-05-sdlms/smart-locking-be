# Waybill OCR upload size (#222)

`POST /api/delivery-requests/{id}:waybill-ocr` accepts a nonempty file of at most **5 MiB (5,242,880 bytes)**. A larger file returns HTTP 400 with `image/invalid_image` and `Image must not exceed 5 MB.` An empty file returns the same code with `Image must not be empty.`

The controller checks the bound file length before opening its stream or calling the delivery service, so a rejected file cannot reach image storage or OCR, change the delivery snapshot, or create a successful scan audit. Guest-session header validation still precedes the file check; the delivery service still validates session ownership/status and scan quotas for accepted files.

The existing **8 MiB total request ceiling** is retained separately. Multipart headers and boundaries count toward the HTTP request size, so setting that ceiling to exactly 5 MiB would reject a valid 5 MiB file. Files of 5 MiB + 1 byte and 5.5 MiB fit within the request ceiling and reach the explicit 400 file validation. Requests above the transport ceiling may be rejected during request/form parsing before controller validation.

No limits on the general image upload endpoint are changed. Image format validation, image storage and OCR behavior for accepted waybill files retain their existing implementation.

`WaybillUploadHttpBoundaryTests` sends real multipart HTTP requests through local Kestrel, MVC form binding, production upload-limit/rate-limit/timeout registration and the real delivery service. It verifies exact 5 MiB acceptance including multipart overhead; 5 MiB + 1 byte, 5.5 MiB and empty-file rejection; zero storage/OCR calls and unchanged snapshot/audit on rejection. A disposable in-memory session database and recording storage/OCR substitutes isolate the length contract; no external provider or persistent image store is used. These tests do not validate JPEG decoding or provider responses.
