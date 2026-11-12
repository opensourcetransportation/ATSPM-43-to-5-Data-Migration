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

Target settings live under `DatabaseConfiguration:<ContextName>`. The migrator builds each connection string from fields such as `DBType`, `Host`, `Database`, `User`, and `Password`. Replace any older `ConnectionStrings` examples with this format. Documented production provider names are:

- `PostgreSql`
- `SqlServer`
- `MySql`
- `Oracle`

Use the provider names shown above for `DBType`. Supply valid settings for all four contexts because the host validates them at startup. The migration phases access these target databases:

| Command or phase | Target contexts used |
| --- | --- |
| `transfer-config` | `ConfigContext` |
| `transfer-events` | `ConfigContext` and `EventLogContext` |
| `transfer-speed` | `ConfigContext` and `EventLogContext` |
| `upgrade-to-5` | `ConfigContext` and `EventLogContext` unless the corresponding phases are skipped |

Example PostgreSQL settings. Replace the `DatabaseConfiguration` section in the bundled `appsettings.json` and keep the other sections, including the source queries. This example uses one database for all four contexts; use the corresponding database names if your ATSPM deployment separates them.

```json
{
  "DatabaseConfiguration": {
    "ConfigContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Port": 5432,
      "Database": "atspm",
      "User": "migration_user",
      "Password": "replace-with-your-password"
    },
    "EventLogContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Port": 5432,
      "Database": "atspm",
      "User": "migration_user",
      "Password": "replace-with-your-password"
    },
    "AggregationContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Port": 5432,
      "Database": "atspm",
      "User": "migration_user",
      "Password": "replace-with-your-password"
    },
    "IdentityContext": {
      "DBType": "PostgreSql",
      "Host": "db01",
      "Port": 5432,
      "Database": "atspm",
      "User": "migration_user",
      "Password": "replace-with-your-password"
    }
  }
}
```

`AggregationContext` and `IdentityContext` must pass startup validation, but the migration phases do not directly access them.

## Environment Variables

.NET maps double underscores to nested configuration keys. This PowerShell example supplies the same four contexts without putting credentials in the settings file. Set `$dbPassword` from your secret store first.

```powershell
foreach ($context in 'ConfigContext', 'EventLogContext', 'AggregationContext', 'IdentityContext') {
    $prefix = "DatabaseConfiguration__${context}__"
    Set-Item "Env:${prefix}DBType" 'PostgreSql'
    Set-Item "Env:${prefix}Host" 'db01'
    Set-Item "Env:${prefix}Database" 'atspm'
    Set-Item "Env:${prefix}User" 'migration_user'
    Set-Item "Env:${prefix}Password" $dbPassword
}
```

The migrator adds the `appsettings.json` beside the executable after the default .NET host sources, including environment variables. It then loads this project's optional .NET user secrets, followed by command-line configuration. Later sources override matching keys. If you supply target settings through environment variables, leave those fields unset in both the bundled file and user secrets. For example, a saved `DatabaseConfiguration:ConfigContext:Host` of `localhost` can override a GCP host in the settings file.

The explicit `--source`, `--start`, and `--end` arguments control the current run. `transfer-config` logs the effective target server and database before making changes; verify they match the environment you intend to update.

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

- [`appsettings.json`](../appsettings.json) contains the starter connection shape, source queries, and column mappings.
- [`nuget.config`](../nuget.config) defines the feeds used to restore the UDOT packages.

Do not put production passwords in committed copies of either documentation examples or `appsettings.json`.
