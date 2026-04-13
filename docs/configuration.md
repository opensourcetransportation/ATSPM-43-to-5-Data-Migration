# Configuration

`DataMigrator` always reads from an ATSPM 4.3 SQL Server source passed on the command line with `--source`.

The destination is not hardcoded. It is determined by the ATSPM 5.2 provider and connection-string configuration loaded by the application.

## Source

- Source must be an ATSPM 4.3 SQL Server connection string.
- The source is provided per run with `--source`.
- There is no source-provider switch in this tool.

Example:

```powershell
--source "Server=sql01;Database=ATSPM;User Id=sa;Password=..."
```

## Destination

- Destination provider comes from the configured ATSPM 5.2 settings.
- Destination connection strings can come from `appsettings.json`, environment variables, or other standard .NET configuration sources.
- Do not assume PostgreSQL by default. Set the target provider explicitly for the environment you are migrating into.

## Local Settings

The starter [appsettings.json](C:\Projects\ATSPM-43-to-5-Data-Migration\appsettings.json) is intended as a local development baseline. Production values should come from environment-specific configuration or secrets.

## Package Restore Note

This repository currently uses [nuget.config](C:\Projects\ATSPM-43-to-5-Data-Migration\nuget.config) to restore missing UDOT package dependencies from the local `local-packages` feed.

That is a temporary workaround until the missing UDOT packages are published to a shared online feed.
