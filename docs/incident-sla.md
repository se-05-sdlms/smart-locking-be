# Incident SLA Escalation Monitor (issue #153)

A background worker (`IncidentSlaWorker`, every `IncidentSla:CheckIntervalSeconds`, default 300) checks open incidents and
escalates the ones that are overdue to Administrators (SRS NSF-10, UC-16). Time is real clock time, 24/7.

## Priority and deadlines

Priority is derived from `Incident.Type` (no new column). Defaults, in minutes:

| Priority | Types | Acknowledge | Resolve |
| --- | --- | --- | --- |
| Critical | `Locker` | 15 | 60 |
| High | `Compartment`, `Payment`, `Retrieval` | 30 | 240 |
| Medium | `Parcel`, `Return`, any unknown type | 120 | 1440 |
| Low | `Other` | 480 | 4320 |

- **Acknowledge** deadline runs from `CreatedAt` and only applies while the incident is `Open` with no assigned operator.
- **Resolve** deadline runs from `CreatedAt` until the incident is `Resolved`.
- At `WarningPercent` (default 80) of a deadline the monitor sends one early-warning notification
  (`IncidentSlaAcknowledgeWarning` / `IncidentSlaResolveWarning`) to the assigned operator, or to the operators assigned to the locker,
  or to Administrators when neither exists. Each milestone is warned once.
- Past a deadline the incident becomes `Escalated` (`EscalatedAt` set), an `IncidentAction` and an `Incident.SlaEscalated` audit log
  (actor = system) are written, every active Administrator gets an `IncidentEscalated` notification and the realtime
  `IncidentUpdated` event is published. An escalated incident is never escalated again, so re-running the job changes nothing.

`Resolved` and `Escalated` incidents are ignored.

## System actor

Automatic actions have no user: `IncidentAction.ActionByUserId` is now nullable (migration `MakeIncidentActionActorNullable`).
The incident detail API keeps its contract and returns the system action with `actionByUserId = 00000000-0000-0000-0000-000000000000`
and `actionByName = "Hệ thống"`.

## Configuration

```text
IncidentSla__Enabled=true
IncidentSla__CheckIntervalSeconds=300
IncidentSla__WarningPercent=80
IncidentSla__Critical__AcknowledgeMinutes=15
IncidentSla__Critical__ResolveMinutes=60
(same pattern for High, Medium, Low)
```

The thresholds are server configuration. Editing them from the Administrator System Policy screen needs new `SystemPolicy`
columns and a policy form change, which is not part of this change.
