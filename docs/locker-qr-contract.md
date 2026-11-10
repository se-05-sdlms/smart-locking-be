# Locker QR contract

Refs #184; FE #82 handles preview, image generation and download separately.

`GET /api/lockers/{id}/qr-context` requires Administrator or the Operator currently authorized to manage the locker. It delegates authorization to locker details, so #195's effective temporary assignment applies to both APIs. Responses contain only `lockerId`, canonical `lockerCode`, `shipperUrl` and operational status; they contain no credentials, access tokens, device identity or unlock rights.

Configure `ShipperWeb__BaseUrl` for each environment as the public Shipper page, without query, fragment or user credentials. HTTPS is required except HTTP loopback for local development. Missing/invalid configuration returns 409 `LOCKER_QR_NOT_CONFIGURED`; no production URL is guessed. Example configuration `https://shipper.example.com/` produces `https://shipper.example.com/?locker=<encoded-code>`.

The query name is `locker`, matching the current Shipper web entry point. The code is URI encoded, and clients must use the returned URL unchanged to render/download a QR image. No BE image dependency or printing feature is needed. The image is public navigation context; opening it still uses the existing guest session/validation flow.

QRs can remain on physical lockers while service is stopped. Shipper resolves the code through the existing public locker endpoint, which returns 404 for an unknown code and 409 `locker/not_operational` for unavailable lockers. #197 additionally exposes maintenance blocks through the shared locker availability query. A QR never bypasses those checks. No FE source is changed in this PR.
