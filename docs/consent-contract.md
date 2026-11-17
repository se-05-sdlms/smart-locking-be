# Registration consent contract

Resident registration accepts the current legal documents before creating an account.

The request includes `termsVersion`, `privacyVersion`, and `consentedAt` (UTC). The
Backend accepts only version `1.0` for both documents, rejects missing or unsupported
versions, and rejects a timestamp more than five minutes in the future. The persisted
`User.ConsentedAt` is the server UTC time; the client value is used only for validity
checking. Existing accounts retain null consent fields and are not modified by the
migration.

The registration response does not expose the consent fields. They remain available
in the account record for audit and future re-consent enforcement.
