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
| Event `--end 2024-01-07` | Includes the entire January 7 date |
| Event `--end 2024-01-07T23:59:59` | Includes the whole day through the stated second |
| Speed `--end 2024-01-07` | Includes the entire January 7 date |
| Speed `--end 2024-01-07T12:00:00` | Stops at the stated inclusive time |

Use ISO `yyyy-MM-dd` date-only end values, such as `2024-01-07`, for consistent behavior across workstations and containers. Date-only end values have the same whole-day meaning for event, speed, and combined migrations.
An explicit midnight value such as `--end 2024-01-07T00:00:00` includes only that instant; use `--end 2024-01-07` to include the entire date.

## `transfer-config`

Imports ATSPM configuration and speed-device configuration. Speed devices are derived only from the latest non-deleted signal version; older versions do not create duplicate devices.

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..."
```

| Option | Required/default | Meaning |
| --- | --- | --- |
| `--source` | Required | ATSPM 4.3 SQL Server connection string |
| `--delete` | Default `false` | Deletes existing target configuration before import; back up the target first |
| `--update-locations` | Default `true` | Enables the main configuration/location import; normally omit because it is already enabled |
| `--update-speed` | Default `true` | Enables speed-device configuration import; this does not migrate speed event rows |

`--delete` removes target configuration records and resets identities on PostgreSQL. The service checks that the source contains `dbo.Signals` before deletion, but operators must still verify both connection strings and take a backup.

To import only speed-device configuration, use `--update-locations false --update-speed true`. This does not populate API keys or `DeviceProperties`. Do not combine a speed-only refresh with `--delete`, because `--delete` clears the complete configuration entity set.

## `transfer-events`

Imports controller events from `dbo.Controller_Event_Log` and stores compressed event windows in the target.

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --locations 1234
```

| Option | Required/default | Meaning |
| --- | --- | --- |
| `--source` | Required | ATSPM 4.3 SQL Server connection string |
| `--start` | Required | Inclusive start date/time |
| `--end` | Required | Inclusive end; a date-only value includes the entire end date |
| `--batch` | Default `500`, maximum `600` | Positive number of compressed event windows written per target batch |
| `--device` | Optional | Integer `DeviceTypes` filter applied while selecting locations; normally omit for controller-event migration |
| `--locations` | Optional | Comma-separated location identifiers, for example `1234,5678` |

Location matching is exact after surrounding whitespace is trimmed. The service uses the latest active target location version and its signal-controller device to resolve each source location.

Rerunning the same logical `(LocationIdentifier, DeviceId, Start)` windows replaces existing compressed rows.

## `transfer-speed`

Imports source speed events and stores compressed speed-event windows in the target.

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
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
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

It accepts the configuration and event options above, plus:

For this combined command, `--source`, `--start`, and `--end` may be omitted when their values are supplied under `UpgradeTo5CommandConfiguration` in appsettings or environment variables. Explicit command-line values take precedence.

| Option | Default | Meaning |
| --- | --- | --- |
| `--skip-config` | `false` | Skip configuration migration |
| `--skip-events` | `false` | Skip controller-event migration |
| `--skip-speed` | `false` | Skip speed-event migration |

Examples:

```powershell
# Configuration only
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --skip-events --skip-speed

# Retry speed only
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --locations 1234 `
  --skip-config --skip-events
```

If a phase fails, the command stops and returns an error; later phases do not run. Completed event and speed windows can be safely retried using the same boundaries.

## Reusable Integration Test

Run [`scripts/integration-test.ps1`](../scripts/integration-test.ps1) against a disposable Docker target to exercise all commands with a one-hour source slice and verify persisted rows and idempotency. See [Integration Testing](integration-testing.md) for prerequisites, parameters, expected target changes, and the faster rerun mode.
