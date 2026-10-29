# SMS delivery boundary (#152)

Hosted OTP issuance uses `ISmsSender` for registration/password reset and all phone OTP purposes. The hosted API always registers the adapter; no provider acceptance means 503 `sms/unavailable` and the new OTP is not saved as valid. Existing challenges are not revoked in the DB on transport failure. Provider acceptance precedes DB persistence, so a later DB failure can leave an unusable message delivered; the caller must retry issuance. Acceptance is not proof the handset received SMS. Existing verification limits/rate-limited auth endpoints remain unchanged.

No vendor has been selected in #152. `Sms__Mode` defaults to `Disabled`, which fails closed. Production must supply a reviewed vendor adapter implementing `ISmsSender`, or an existing trusted relay implementing the protocol below. This PR neither chooses/bills a vendor nor claims SMS was sent on a real network.

For local/demo use set ASPNETCORE_ENVIRONMENT=Development (or Demo), `Sms__Mode=Sandbox`, and explicitly opt into `Otp__LogCode=true` to see local codes. Sandbox is rejected in all other host environments. The former unguarded OTP logging has been removed; setting `Otp__LogCode=true` alone cannot expose Production OTPs. Sandbox acceptance simulates delivery only.

## Optional relay contract

`HttpRelay` is a documented adapter protocol for a relay controlled by the deploying team, **not** a vendor API endpoint. Configure `Sms__RelayEndpoint` as HTTPS without query/userinfo/fragment and `Sms__RelayApiKey` through secrets. No redirects, implicit retries or provider body logging are permitted. POST includes `Authorization: Bearer <secret>`, `Idempotency-Key: <challengeId N-format>`, and JSON `{ messageId, destinationPhone, code, purpose, expiresInSeconds: 600 }`. Destination is the existing normalized Vietnamese local phone number; the relay must translate it according to its selected vendor. It must deduplicate the message ID, honor the expiry, and return successful HTTP with `{ accepted: true, messageId: "<provider receipt>" }` only after the provider accepts. Any failure, invalid response or timeout rejects issuance. Provider credentials/bodies must never be committed or logged.

Legacy code directly constructing the challenge store remains source compatible. Hosted delivery is guaranteed by DI registration and requires `IHostEnvironment`; existing challenge-only tests do not verify the transport. Live provider delivery and approved deployment configuration remain outstanding until the team selects/provisions SMS.
