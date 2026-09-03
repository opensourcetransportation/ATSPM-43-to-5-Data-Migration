# ATSPM 4.3 to 5 Data Migration

`DataMigrator` is a .NET 8 utility for moving data from an ATSPM 4.3 SQL Server database into a configured ATSPM 5 environment.

## What It Migrates

- Configuration: locations, approaches, detectors, controller devices, areas, routes, and related records
- Speed-device configuration derived from the latest non-deleted ATSPM 4.3 signal version
- Controller event logs from `dbo.Controller_Event_Log`
- Speed events from `dbo.Speed_Events`
- All three phases through the guided `upgrade-to-5` command

The source is always ATSPM 4.3 on SQL Server. The target provider and database are selected through the ATSPM connection-string configuration.

## Before You Run It

- Download the published Windows executable, use the container image, or install the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8) to build from source.
- Confirm network access to both databases.
- Give the source login `SELECT` access to the ATSPM 4.3 objects being migrated.
- Give the target login read/write access. PostgreSQL configuration migration also requires permission to apply EF migrations and alter the configuration schema.
- Back up the target configuration database before using `--delete`. That option removes existing target configuration before importing it again.
- Start with one known location and a short time range before running a production-sized migration.

See [Configuration](docs/configuration.md) for connection strings, provider names, permissions, and schema behavior.

## 1. Configure the Target

Set at least `ConfigContext` for configuration migration and `EventLogContext` for event or speed migration:

```json
"ConnectionStrings": {
  "ConfigContext": {
    "Provider": "PostgreSql",
    "ConnectionString": "Host=db01;Database=atspm;Username=...;Password=..."
  },
  "EventLogContext": {
    "Provider": "PostgreSql",
    "ConnectionString": "Host=db01;Database=atspm;Username=...;Password=..."
  }
}
```

Documented production target provider names are `PostgreSql`, `SqlServer`, `MySql`, and `Oracle`. Do not commit credentials; use environment variables or another supported .NET configuration source in production.

ATSPM Docker development environments can also supply their existing `DatabaseConfiguration:*` settings through user secrets or `DatabaseConfiguration__*` environment variables. When the migrator runs in a container, configure database hosts as Docker service/container names or another address reachable from that container; `localhost` refers to the migrator container itself.

## 2. Migrate Configuration

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..."
```

This imports the main configuration and speed-device configuration by default. To reload only speed devices without deleting locations, detectors, controller devices, or other configuration, run:

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --update-locations false `
  --update-speed true
```

Speed-device selection uses exactly one row per `SignalID`: the latest non-deleted `Signals` version, ordered by `Start` and then `VersionID`. The latest version must have a detection-type `3` detector and a nonzero numeric latitude. Older versions do not create extra devices, and a signal whose latest version no longer qualifies is omitted. The import assigns the shared `Speed` device configuration and a placeholder IP address of `127.0.0.1`; it does not migrate speed-event rows, API keys, or `DeviceProperties`.

For a clean replacement, first take a backup and then add `--delete`.

`--delete` removes the full target configuration set, not only speed devices. Do not use it for a speed-device-only refresh.

## 3. Validate a Small Event Slice

Event start and end values are inclusive. A date-only end value includes that entire date:

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --locations 1234
```

Events are processed in hourly windows. Rerunning the same windows replaces matching compressed records instead of intentionally appending duplicates.

## 4. Migrate Speed Events

For speed migration, a date-only end value includes that entire date:

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01 `
  --end 2024-01-07 `
  --locations 1234
```

## 5. Run the Combined Workflow

After validating each phase, the combined command runs configuration, controller events, and speed events in sequence:

```powershell
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

Use `--skip-config`, `--skip-events`, or `--skip-speed` to omit a phase. Run `dotnet run --project . -- <command> --help` (or `DataMigrator.exe <command> --help`) for CLI help.

## Documentation

- [Installation](docs/installation.md)
- [Configuration and permissions](docs/configuration.md)
- [Commands and option reference](docs/usage.md)
- [Production upgrade and validation guide](docs/upgrade-guide.md)
- [Reusable integration test](docs/integration-testing.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Implementation history and design notes](docs/design/data-migrator-extraction-plan.md)
