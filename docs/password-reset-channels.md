# Password reset channels

The existing endpoints remain `POST /api/auth/forgot-password` and
`POST /api/auth/reset-password`, with `loginIdentifier` in both requests.

For Administrator and LockerOperator accounts, submitting the normalized email
selects email OTP for both issuance and verification, including accounts that also
have a phone number. Delivery errors propagate; email requests never fall back to
SMS. The web forgot-password flow accepts email only. An unknown account retains
the existing generic acknowledgement and cannot reset a password.

An explicit phone identifier retains the existing SMS reset flow. The resident
reset contract is unchanged. A phone challenge cannot satisfy an internal email
reset request. Existing OTP expiry, attempt limits and single consumption still
apply. A successful reset updates the password, clears MustChangePassword and
records ResetPassword audit data.

Regression tests execute AuthService and OtpService with an isolated EF in-memory
store and replace only email/SMS delivery. They verify channel selection, invalid
and reused OTP rejection, password/audit effects and phone compatibility. These
tests do not establish provider delivery or inbox receipt.
