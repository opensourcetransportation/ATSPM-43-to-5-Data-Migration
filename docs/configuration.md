# Configuration and Permissions

`DataMigrator` reads from ATSPM 4.3 on SQL Server and writes to a configured ATSPM 5 target.

## Source SQL Server

Pass the source connection string to every command with `--source`:

```powershell
--source "Server=sql01;Database=ATSPM43;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True"
```

The login needs connection permission and `SELECT` access to the objects used by the selected phase:

- Configuration: `Signals`, `Approaches`, `Detectors`, `DetectionTypeDetector`, `Jurisdictions`, `Region`, `Areas`, `AreaSignals`, `Routes`, `RouteSignals`, `RoutePhaseDirections`, and `ControllerTypes`
- Controller events: `dbo.Controller_Event_Log`
- Speed events: `dbo.Speed_Events`; detector and device resolution comes from the migrated target configuration

The configured queries are in [`appsettings.json`](../appsettings.json). If the source uses a different database name, schema, or customized 4.3 layout, update `LocationQueries` before migrating configuration.

Configuration source reads use a 300-second command timeout. Controller-event reads use 120 seconds, and speed-event reads use 300 seconds. A timeout normally indicates a source indexing, blocking, network, or range-size problem; see [Troubleshooting](troubleshooting.md).

## Target Connections

Target settings live under `DatabaseConfiguration`. Each context is configured with `DBType`, `Host`, `Database`, `User`, `Password`, and optional provider-specific `Options`. Documented production provider names are:

- `PostgreSql`
- `SqlServer`
- `MySql`
- `Oracle`

Configure all four contexts. The migration phases use the contexts below, but the ATSPM infrastructure validates all four context configurations during startup:

| Command or phase | Required target context |
| --- | --- |
| `transfer-config` | `ConfigContext` |
| `transfer-events` | `ConfigContext` and `EventLogContext` |
| `transfer-speed` | `ConfigContext` and `EventLogContext` |
| `upgrade-to-5` | `ConfigContext` and `EventLogContext` unless the corresponding phases are skipped |

Example PostgreSQL configuration:

```json
{
  "DatabaseConfiguration": {
    "ConfigContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Database": "atspm",
      "User": "...",
      "Password": "..."
    },
    "EventLogContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Database": "atspm",
      "User": "...",
      "Password": "..."
    },
    "AggregationContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Database": "atspm",
      "User": "...",
      "Password": "..."
    },
    "IdentityContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Database": "atspm",
      "User": "...",
      "Password": "..."
    }
  }
}
```

`AggregationContext` and `IdentityContext` are not directly used by these migration phases, but they must still be present and valid because the ATSPM infrastructure validates all four database contexts during startup.

## Environment Variables

.NET maps double underscores to nested configuration keys. For example:

```powershell
$env:DatabaseConfiguration__ConfigContext__DBType = "PostgreSql"
$env:DatabaseConfiguration__ConfigContext__Host = "db01"
$env:DatabaseConfiguration__ConfigContext__Database = "atspm"
$env:DatabaseConfiguration__ConfigContext__User = "..."
$env:DatabaseConfiguration__ConfigContext__Password = "..."
$env:DatabaseConfiguration__EventLogContext__DBType = "PostgreSql"
$env:DatabaseConfiguration__EventLogContext__Host = "db01"
$env:DatabaseConfiguration__EventLogContext__Database = "atspm"
$env:DatabaseConfiguration__EventLogContext__User = "..."
$env:DatabaseConfiguration__EventLogContext__Password = "..."
$env:DatabaseConfiguration__AggregationContext__DBType = "PostgreSql"
$env:DatabaseConfiguration__AggregationContext__Host = "db01"
$env:DatabaseConfiguration__AggregationContext__Database = "atspm"
$env:DatabaseConfiguration__AggregationContext__User = "..."
$env:DatabaseConfiguration__AggregationContext__Password = "..."
$env:DatabaseConfiguration__IdentityContext__DBType = "PostgreSql"
$env:DatabaseConfiguration__IdentityContext__Host = "db01"
$env:DatabaseConfiguration__IdentityContext__Database = "atspm"
$env:DatabaseConfiguration__IdentityContext__User = "..."
$env:DatabaseConfiguration__IdentityContext__Password = "..."
```

The application loads the standard .NET host configuration sources, the local `appsettings.json`, optional user secrets, environment variables, and command-line values. Prefer an environment-specific secret store or environment variables for production credentials. The explicit command-line `--source`, `--start`, and `--end` values control the current run.

## Target Permissions and Schema

The target login needs permission to read, insert, update, and delete rows in the configuration and compressed-event tables used by the selected phases.

For PostgreSQL targets, `transfer-config` also:

1. Applies pending `ConfigContext` EF migrations.
2. Applies the idempotent `20260521163837_5_3` compatibility bridge when needed.
3. Deletes the documented configuration entity set in a transaction when `--delete` is selected.

The PostgreSQL login therefore needs the DDL and ownership privileges required for those operations. Other providers do not receive automatic schema migration from this service; apply the target application's schema before running the migrator.

## Destructive Configuration Replacement

`--delete` is not a dry run. It removes existing target route locations, routes, devices, detectors, approaches, locations, areas, jurisdictions, regions, device configurations, and products before importing source configuration. It does not issue an unbounded provider-specific `CASCADE` command.

Before using it:

1. Back up the target configuration database.
2. Verify that `--source` points to the intended ATSPM 4.3 database.
3. Verify the target provider and connection string.
4. Run without `--delete` or against a disposable target first when possible.

The service performs a source-schema preflight before deleting target configuration, but that check is not a substitute for a backup.

## Repository Configuration Files

- [`appsettings.json`](../appsettings.json) contains the starter database configuration shape, source queries, and column mappings.
- [`nuget.config`](../nuget.config) defines the feeds used to restore the UDOT packages.

Do not put production passwords in committed copies of either documentation examples or `appsettings.json`.
