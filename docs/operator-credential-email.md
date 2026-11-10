# Operator credential email

Configure `Email__Smtp__Host`, `Email__Smtp__Port` (typically 587 with STARTTLS),
`Email__Smtp__From`, `Email__Smtp__Username`, and `Email__Smtp__Password` through
deployment secrets. Apply the `AddEmailOtpDestination` migration before deploying.
No SMTP password or generated credential belongs in appsettings or logs.

`POST /api/users` emails the initial temporary password. For Locker Operator accounts,
the server generates it even if the request contains `password`. The response no longer
contains `temporaryPassword`; the frontend should remove any password display.
The operator must change the password after login. If email delivery fails, the account
remains created and the API returns `user/email_unavailable`; an administrator can call
`POST /api/users/{operatorId}/temporary-password` to generate and email a new one.
That endpoint returns 204, revokes refresh tokens, and logs the action without the password.

Email-only Operator and Administrator accounts can use the existing
`POST /api/auth/forgot-password` and `POST /api/auth/reset-password` flow. The reset code
is delivered to their email, stored only as a hash, and expires after 10 minutes.
Accounts with a phone number continue using the phone OTP flow.
