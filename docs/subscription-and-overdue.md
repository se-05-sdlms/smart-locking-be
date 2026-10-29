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

Plan requests use `code`, `name`, `description`, `price`, `durationDays`, and `maxActiveParcels`. Updates omit `code` and include `isActive`. Deactivation preserves purchased history. Prices must be whole VND; paid checkout requires a positive amount. A zero-priced plan skips PayOS: `POST /api/resident-subscriptions/subscribe` activates it immediately and returns `{ activated: true, checkoutUrl: "", orderCode: 0, amount: 0 }`. It queues after the resident's current period, and the same free plan cannot be held twice at once (409 `subscription/already_exists`), so free periods cannot be farmed.

## Lifecycle

A pending subscription is saved before PayOS is called. Its price and duration are captured at purchase, so later plan edits do not alter that purchase. Pending dates are a duration snapshot; only Active rows whose start date has arrived grant a current period.

The webhook verifies the full SDK payload including bank fields, then checks the signed result code, amount, currency, and reference. Authenticated unknown orders are acknowledged without mutation, including the PayOS registration sample. Activation locks the resident row in PostgreSQL so concurrent successful purchases stack after the latest active end date. Replayed payments never extend the interval, including after expiration. An hourly worker expires elapsed Active rows with a conditional database update.

Provider failures leave a pending record because a timeout cannot prove that PayOS did not create the link. A valid later webhook can still settle that order. Automatic checkout reconciliation/retry, pending cancellation, and overdue-fee payment endpoints are outside this plan. Do not close issue #26 solely from this subscription implementation: that issue also requires overdue-payment history and reconciliation.

## Parcel cutoff

`MaxStorageUntil` is the persisted authoritative deadline produced by the existing system policy. This change does not recalculate historical deadlines or change policy duration. At that deadline remote pickup is rejected with HTTP 409 and the locker recovery address. Fees stop accumulating at that instant. In the final 24 hours a separate clearance warning is sent once per parcel, independent of the ordinary daily reminder.

## Verification limits

Tests cover real SDK checksum verification with signed fixtures, invalid/tampered data, subscription stacking/replay, captured purchase terms, expiry, ownership, cutoff boundaries, fee capping, and scoped clearance queries. Offline checkout tests cover validation/cancellation; a real provider checkout and PostgreSQL concurrency still need an integration environment. No real payment was created during implementation.

## Resident package rules for parcel intake (issue #157)

Residents choose a package at registration. The Backend records it through the normal subscription checkout, called right after
registration with the new access token (`POST /api/resident-subscriptions/subscribe`), so `POST /api/auth/register` is unchanged.
The pending subscription is saved before PayOS is called; the package takes effect once the verified webhook activates it.

A resident can receive a new parcel only when both hold:

1. They have an `Active` subscription whose period is in effect now (a stacked future period or an expired one does not count).
2. The number of parcels they currently have is below the plan `MaxActiveParcels`. Parcels in `Stored` or `Overdue` status count, and so do delivery
   requests that will become parcels (`PendingApproval`, `Approved`, `Allocated`). Retrieved or removed parcels and rejected, expired or
   cancelled requests free capacity.

Parcels already in a locker can always be picked up, whatever the subscription status.

Where it is enforced (`ParcelQuotaService`):

| Step | Result when the resident cannot receive |
| --- | --- |
| `POST /api/delivery-requests/{id}:resolve-recipient` | HTTP 409 `delivery/recipient_cannot_receive`, so the Shipper knows before sending the request |
| `POST /api/delivery-requests/{id}:submit` | HTTP 409 `delivery/recipient_cannot_receive` |

The message is intentionally generic so a Shipper cannot learn a resident's plan or parcel count.

Set `ResidentSubscription__EnforceParcelLimits=false` to switch the rule off, for example in development databases that have no plans yet.
It is on by default, so a deployment where residents have no subscription will refuse new parcels until they subscribe.
