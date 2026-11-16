# Initial operator assignment (#221)

Creating a locker operator with an initial locker uses the same validation as
assigning an existing operator: an empty locker ID returns 400, a missing locker
returns 404 `locker/not_found`, and a locker with a non-revoked base assignment
returns 409 `user/assignment_conflict`.

The existing PostgreSQL allocation advisory lock serializes account creation
with create/assign/revoke operations. Validation runs before adding account,
profile or audit entities. A relational transaction commits the account,
profile, initial assignment and creation audit together; the unique active
assignment index remains in place. A losing request sends no credential email.

Credential delivery follows commit, retaining the existing recovery contract:
if delivery fails, 503 `user/email_unavailable` explains that the account exists
and the administrator should reissue its temporary password. No initial locker
continues to allow an unassigned operator. Email syntax validation is tracked
separately in #220.

Eight migrated disposable PostgreSQL regressions exercise occupied, unknown and
empty lockers; successful creation and read-back; unassigned creation; competing
creates; creation racing existing-user assignment; and delivery failure after
commit. Email and password hashing are test doubles. No shared application DB,
real email provider or production deployment is used for these tests.
