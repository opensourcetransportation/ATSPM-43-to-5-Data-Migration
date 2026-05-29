# Configuration

`DataMigrator` always reads from an ATSPM 4.3 SQL Server source passed on the command line with `--source`.

The destination is not hardcoded. It is determined by the ATSPM 5.3 provider and connection-string configuration loaded by the application.

## Source

- Source must be an ATSPM 4.3 SQL Server connection string.
- The source is provided per run with `--source`.
- There is no source-provider switch in this tool.

Example:

```powershell
--source "Server=sql01;Database=ATSPM;User Id=sa;Password=..."
```

## Destination

- Destination provider comes from the configured ATSPM 5.3 settings.
- Destination connection strings can come from `appsettings.json`, environment variables, or other standard .NET configuration sources.
- Do not assume PostgreSQL by default. Set the target provider explicitly for the environment you are migrating into.

## Local Settings

The starter [appsettings.json](C:\Projects\ATSPM-43-to-5-Data-Migration\appsettings.json) is intended as a local development baseline. Production values should come from environment-specific configuration or secrets.

## Package Restore Note

This repository uses [nuget.config](C:\Projects\ATSPM-43-to-5-Data-Migration\nuget.config) to restore UDOT package dependencies from the shared online feeds.

The previous local `local-packages` workaround is no longer required now that the UDOT packages have been republished.

For PostgreSQL targets, the migrator applies pending UDOT `ConfigContext` EF migrations before config import so the target schema matches the republished package model. The current `5.3.0-rc5` PostgreSQL provider package does not expose the official `20260521163837_5_3` config migration through EF migration metadata, so the migrator also applies an idempotent bridge for those schema changes.
