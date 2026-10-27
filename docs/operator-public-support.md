# Public locker support contacts

Depends on #195 effective operator permissions. No account login phone is published.

- Admin `GET/PUT /api/operators/{id}/support-contact`: configure explicit public work name/phone, opt-in `isPublic`, instructions and support hours. `weekdaysMask` uses Sunday bit 0 through Saturday bit 6; times are minutes after local midnight with an explicit fixed UTC offset. Intervals are start-inclusive/end-exclusive, including overnight shifts; 00:00–24:00 covers the whole selected day.
- Admin `GET/PUT /api/lockers/{id}/support-settings`: configure a fallback name, optional work phone/hours and required instructions.
- Anonymous rate-limited `GET /api/lockers/public/by-code/{code}/support`: returns only locker code, availability/fallback flags, public contact name/phone/hours/instructions and refresh time. It resolves the effective operator on every request, including approved active temporary cover. No operator IDs, email, account phone, roles or operational details are returned.
- Authenticated `GET /api/lockers/{id}/support`: same response. Administrators can access all; operators need current effective permission; residents need their registered locker or their delivery/return/incident context.

Missing or unpublished contact, absent operator and closed support hours resolve to the configured fallback. Missing fallback phone is represented by `isAvailable: false`, so clients display instructions without a broken call action. No guessed number or address is substituted. Contacts remain separate from parcel recovery addresses.

Clients should refresh when entering the support context and on focus, and offer call/copy actions only for a returned phone. Locker code and a non-secret transaction reference can be copied separately; pickup credentials/tokens must never be included in a support link.

Migration only creates the contact/settings tables; no legacy contact data is inferred or backfilled. Shared database migration application remains a deployment step.
