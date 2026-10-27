# Administrator reports (issue #146, SRS UC-27)

All endpoints are `Administrator` only. Times are UTC and echoed in the response (`timeZone: "UTC"`, `generatedAt`).

## Endpoints

| Endpoint | Behavior |
| --- | --- |
| `GET /api/reports/summary` | Unchanged: six totals, optional one-row CSV through `Accept: text/csv`. The dashboard keeps using it. |
| `GET /api/reports/{type}` | Detailed report, filtered and paged. |
| `GET /api/reports/{type}/export` | CSV of exactly the same filtered data, without paging. |

Query parameters: `from`, `to` (default: last 30 days ending now, at most 366 days), `lockerId`, `status`, and for the detail
endpoint `page` (default 1) and `pageSize` (default 50, maximum 200). `status` is validated per report and is case-insensitive.

## Report types

`type` accepts the names below, with or without dashes and in any case (`locker-utilization`, `LockerUtilization`).

| type | Rows | Date filter applies to | `status` values |
| --- | --- | --- | --- |
| `parcels` | Parcels | `StoredAt` | `ParcelStatus` |
| `returns` | Return requests | `CreatedAt` | `ReturnRequestStatus` |
| `locker-utilization` | One row per locker: compartments total, usable, occupied, utilization % | none (current snapshot) | `LockerOperationalStatus` |
| `incidents` | Incidents (read-only) | `CreatedAt` | `IncidentStatus` |
| `maintenance` | Maintenance requests | `CreatedAt` | `MaintenanceStatus` |
| `overdue-transfers` | Parcels an operator removed from a locker (overdue parcels moved to the collection point) | `RemovedAt` | `Removed` only |
| `overdue-fees` | Overdue charges | `CreatedAt` | `OverdueChargeStatus` |
| `payments` | Overdue-fee payment transactions (PayOS). Resident package purchases are not included. | `RequestedAt` | `PaymentTransactionStatus` |
| `operational-activities` | Locker events (connection, door, status, alerts) | `OccurredAt` | `LockerEventSeverity` |

## Response

```text
{ type, from, to, timeZone, generatedAt,
  columns: [{ key, label, dataType }],   // dataType: text | number | money | datetime | percent
  rows: [{ <column key>: value }],       // one object per row, keys match columns
  totalCount, page, pageSize,
  statusBreakdown: { "<status>": count },  // over the whole filtered set, not just the page
  totalAmount }                            // sum for money reports (overdue-fees, payments), else null
```

Labels are Vietnamese and ready to render as table headers. Rows are ordered newest first.

## CSV export

UTF-8 with a BOM (opens correctly in Excel), headers are the column labels, dates are UTC `yyyy-MM-dd HH:mm:ss'Z'`.
Text cells that start with `=`, `+`, `-` or `@` are prefixed with `'` so exported data cannot run as a spreadsheet formula.
At most 50,000 rows are exported; when the filter matches more, the response carries the header `X-Report-Truncated: true`.
