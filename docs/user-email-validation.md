# Administrator-managed account email validation (#220)

Creating an account with a dotless email such as `operator@sdlms` previously saved the account before attempting credential delivery. The SMTP failure could therefore leave an account behind while returning 503. Updating a staff account also accepted malformed email addresses.

`UserService` now validates a normalized email before creating account/profile/assignment/audit records or sending credentials. Staff updates validate an explicitly supplied email before changing the account or profile. Invalid input returns the existing 400 `validation/invalid_input` response with an `email` field error, which the web form can display.

Addresses are trimmed and lowercased as before. Validation requires a standalone mailbox address and a dotted DNS host with valid labels; it rejects display names, IP hosts, empty labels and labels beginning/ending with a hyphen. Limits are 254 characters overall, 64 for the local part and 63 per DNS label. Aliases, subdomains and ASCII punycode hosts remain supported. This validates syntax; it does not check DNS or guarantee delivery. A valid address whose email provider fails still follows the existing account-created/email-failed recovery behavior.

Omitting or supplying a blank email in a partial staff update retains the existing email. Duplicate normalized addresses still return 409. Resident self-service and authentication flows are outside this change. No migration or stored-address rewrite is needed.

`UserEmailValidationPostgresTests` uses disposable migrated PostgreSQL databases and an email test double. Its four cases verify invalid creates leave no account/profile/assignment/audit/email side effects, valid normalized creates and duplicates, rejected updates preserving records, and valid/omitted updates. On the old source, the invalid create/update cases fail while both valid cases pass. No real email is sent by these tests.
