# Registration OTP lockout regression (#230)

Registration owns the transaction that consumes an OTP and creates the account,
resident profile, audit event and refresh token. An invalid OTP is rejected before
any account changes are staged. That rejection commits only the failed-attempt
counter and optional lockout audit event, then returns `auth/invalid_otp`. Successful
consumption remains in the account transaction, so a subsequent persistence failure
rolls consumption back and leaves the OTP available for retry.

OTP verification locks the challenge row and reloads its state before checking it.
Concurrent failures cannot overwrite each other's counters, and concurrent correct
requests cannot consume the same challenge twice. Verification outside a caller
transaction owns and commits its own transaction; callers with an outer transaction
remain responsible for committing rejected attempts (registration and phone-change
submission do so). The PostgreSQL lock is held until that transaction ends.

## PostgreSQL regression suite

`smart-locking-be.Tests/Auth/OtpPostgresTests.cs` creates one random disposable
database per test using the account in `SMART_LOCKING_TEST_POSTGRES`. The account
must be allowed to create databases. The fixture always connects to `postgres` for
database administration and never changes the configured application database.
The random database is dropped in fixture cleanup. Use a local or isolated CI
PostgreSQL instance; do not point the fixture at a production server.

```powershell
# Supply your local test account; the value below is the isolated CI example only.
$env:SMART_LOCKING_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=test-only-password'
dotnet test smart-locking-be.sln --configuration Release
```

CI provisions PostgreSQL 17 and sets the variable for the full suite. Without the
variable, the five explicitly marked PostgreSQL tests report skipped; an InMemory
run alone does not validate the transaction/concurrency fix.

Covered invariants:

- Each failed registration increments a durable counter in a fresh request context;
  after five failures the correct sixth code is rejected for the 15-minute lockout.
- Concurrent invalid attempts stop at the lockout threshold with one lockout event.
- Concurrent correct verifications consume the challenge exactly once.
- Successful registration commits account, profile, refresh token and consumption.
- Account persistence failure rolls back consumption and account writes together.

Validation on 16 November 2026: PostgreSQL 17, full suite 65 passed / 0 failed /
0 skipped. Applying the two production files from baseline `34f03dd` makes both
the durable-registration-counter test and concurrent-correct-verification test fail;
restoring the fix makes the full suite pass. No shared database or SMS was used.
