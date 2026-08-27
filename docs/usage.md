# Commands and Option Reference

Run commands as `DataMigrator.exe <command>` from a release, or as `dotnet run --project . -- <command>` from the repository.

```powershell
dotnet run --project . -- --help
dotnet run --project . -- transfer-events --help
```

The `upgrade-to-5` command runs the complete migration workflow against the configured ATSPM 5 target.

## Date and Time Rules

Start and end values are inclusive. Processing is divided into hourly windows.

| Input | Behavior |
| --- | --- |
| Event `--end 2024-01-07` | Includes midnight at the start of January 7, not the whole day |
| Event `--end 2024-01-07T23:59:59` | Includes the whole day through the stated second |
| Speed `--end 2024-01-07` | Includes the entire January 7 date |
| Speed `--end 2024-01-07T12:00:00` | Stops at the stated inclusive time |

For the combined command, use an explicit end-of-day timestamp when both event and speed phases should cover whole days.

## `transfer-config`

Imports ATSPM configuration and speed-device configuration.

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=MOE;User Id=...;Password=..."
```

| Option | Required/default | Meaning |
| --- | --- | --- |
| `--source` | Required | ATSPM 4.3 SQL Server connection string |
| `--delete` | Default `false` | Deletes existing target configuration before import; back up the target first |
| `--update-locations` | Default `true` | Enables the main configuration/location import; normally omit because it is already enabled |
| `--update-speed` | Default `true` | Enables speed-device configuration import; this does not migrate speed event rows |

`--delete` removes target configuration records and resets identities on PostgreSQL. The service checks that the source contains `dbo.Signals` before deletion, but operators must still verify both connection strings and take a backup.

## `transfer-events`

Imports controller events from `dbo.Controller_Event_Log` and stores compressed event windows in the target.

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=MOE;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --locations 1234
```

| Option | Required/default | Meaning |
| --- | --- | --- |
| `--source` | Required | ATSPM 4.3 SQL Server connection string |
| `--start` | Required | Inclusive start date/time |
| `--end` | Required | Inclusive end date/time; date-only values do not imply the whole day |
| `--batch` | Default `500` | Positive number of compressed event windows written per target batch |
| `--device` | Optional | Integer `DeviceTypes` filter applied while selecting locations; normally omit for controller-event migration |
| `--locations` | Optional | Comma-separated location identifiers, for example `1234,5678` |

Location matching is exact after surrounding whitespace is trimmed. The service uses the latest active target location version and its signal-controller device to resolve each source location.

Rerunning the same logical `(LocationIdentifier, DeviceId, Start)` windows replaces existing compressed rows.

## `transfer-speed`

Imports source speed events and stores compressed speed-event windows in the target.

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=MOE;User Id=...;Password=..." `
  --start 2024-01-01 `
  --end 2024-01-07 `
  --locations 1234
```

| Option | Required/default | Meaning |
| --- | --- | --- |
| `--source` | Required | ATSPM 4.3 SQL Server connection string |
| `--start` | Required | Inclusive start date/time |
| `--end` | Required | Inclusive end; a date-only value includes the entire end date |
| `--locations` | Optional | Comma-separated location identifiers |

The service resolves detectors and the speed-sensor device through the latest active target configuration. Run configuration migration first.

Rerunning the same logical `(LocationIdentifier, DeviceId, Start)` windows replaces existing compressed rows.

## `upgrade-to-5`

Runs configuration, controller-event, and speed-event phases in that order.

```powershell
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=MOE;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

It accepts the configuration and event options above, plus:

| Option | Default | Meaning |
| --- | --- | --- |
| `--skip-config` | `false` | Skip configuration migration |
| `--skip-events` | `false` | Skip controller-event migration |
| `--skip-speed` | `false` | Skip speed-event migration |

Examples:

```powershell
# Configuration only
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=MOE;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --skip-events --skip-speed

# Retry speed only
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=MOE;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --locations 1234 `
  --skip-config --skip-events
```

If a phase fails, the command stops and returns an error; later phases do not run. Completed event and speed windows can be safely retried using the same boundaries.
