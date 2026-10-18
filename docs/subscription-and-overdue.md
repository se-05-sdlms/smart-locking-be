# Subscription checkout and overdue handling

## Configuration

Apply the existing `AddSubscriptionSystem` EF migration through the normal deployment process. This change does not automatically migrate a database.

Provide secrets through the environment or .NET user secrets:

```text
PayOS__ClientId
PayOS__ApiKey
PayOS__ChecksumKey
PayOS__ReturnUrl=https://your-app.example/subscriptions/success
PayOS__CancelUrl=https://your-app.example/subscriptions/cancel
```

The return/cancel URLs are server configuration, never supplied by a caller. Neither redirect activates a subscription. Configure the PayOS channel webhook to `POST /api/webhooks/payos`.

## Endpoints

| Endpoint | Access | Behavior |
| --- | --- | --- |
| `GET /api/subscription-plans` | Public | Active plans |
| `GET /api/admin/subscription-plans` | Administrator | All plans |
| `POST /api/admin/subscription-plans` | Administrator | Create a plan |
| `PUT /api/admin/subscription-plans/{id}` | Administrator | Update or deactivate with `isActive: false` |
| `POST /api/resident-subscriptions/subscribe` | Resident | Body `{ "planId": "UUID" }`; returns subscription ID, order code, amount, checkout URL |
| `GET /api/resident-subscriptions/current` | Resident | Currently effective subscription, excluding future stacked periods |
| `GET /api/resident-subscriptions/history` | Resident | Own subscriptions only |
| `POST /api/webhooks/payos` | Anonymous, verified signature | Activates a matching successful VND payment |
| `GET /api/parcels?view=ClearancePending` | Operator or Administrator | Stored/Overdue parcels at or beyond `MaxStorageUntil`; operators retain locker assignment scope |

Plan requests use `code`, `name`, `description`, `price`, `durationDays`, and `maxActiveParcels`. Updates omit `code` and include `isActive`. Deactivation preserves purchased history. Prices must be whole VND; paid checkout requires a positive amount (zero-priced plans cannot use this checkout).

## Lifecycle

A pending subscription is saved before PayOS is called. Its price and duration are captured at purchase, so later plan edits do not alter that purchase. Pending dates are a duration snapshot; only Active rows whose start date has arrived grant a current period.

The webhook verifies the full SDK payload including bank fields, then checks the signed result code, amount, currency, and reference. Authenticated unknown orders are acknowledged without mutation, including the PayOS registration sample. Activation locks the resident row in PostgreSQL so concurrent successful purchases stack after the latest active end date. Replayed payments never extend the interval, including after expiration. An hourly worker expires elapsed Active rows with a conditional database update.

Provider failures leave a pending record because a timeout cannot prove that PayOS did not create the link. A valid later webhook can still settle that order. Automatic checkout reconciliation/retry, pending cancellation, and overdue-fee payment endpoints are outside this plan. Do not close issue #26 solely from this subscription implementation: that issue also requires overdue-payment history and reconciliation.

## Parcel cutoff

`MaxStorageUntil` is the persisted authoritative deadline produced by the existing system policy. This change does not recalculate historical deadlines or change policy duration. At that deadline remote pickup is rejected with HTTP 409 and the locker recovery address. Fees stop accumulating at that instant. In the final 24 hours a separate clearance warning is sent once per parcel, independent of the ordinary daily reminder.

## Verification limits

Tests cover real SDK checksum verification with signed fixtures, invalid/tampered data, subscription stacking/replay, captured purchase terms, expiry, ownership, cutoff boundaries, fee capping, and scoped clearance queries. Offline checkout tests cover validation/cancellation; a real provider checkout and PostgreSQL concurrency still need an integration environment. No real payment was created during implementation.
