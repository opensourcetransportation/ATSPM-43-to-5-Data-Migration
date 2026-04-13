# ATSPM-43-to-5-Data-Migration

`DataMigrator` is a .NET 8 utility for moving ATSPM 4.3 data from SQL Server into an ATSPM 5.2 environment.

## What It Does

- Migrates configuration data from ATSPM 4.3 SQL Server into a configured ATSPM 5.2 target
- Migrates controller event logs into the configured ATSPM 5.2 event-log store
- Migrates speed events into the configured ATSPM 5.2 event-log store
- Provides a guided `upgrade-to-5-2` command for the standard upgrade flow

## Source And Target Rules

- Source is always ATSPM 4.3 on SQL Server
- Target provider is determined by your configured ATSPM 5.2 connection strings and provider settings
- No target database provider is assumed by default

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8) (for source builds) or the published Windows EXE from GitHub Releases
- Network access to the ATSPM 4.3 SQL Server source database
- A running ATSPM 5.2 environment with the target database schema already applied
- Connection strings and provider name for the ATSPM 5.2 target

## Getting Started

### 1. Configure the target database

Open `appsettings.json` and fill in the connection strings for your ATSPM 5.2 target. Set `Provider` to match your database engine (`PostgreSql`, `SqlServer`, `MySql`, or `Oracle`).

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

For sensitive environments use [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) or environment variables instead of editing `appsettings.json` directly.

### 2. Migrate configuration

Run this first. It imports locations, devices, approaches, detectors, and all related configuration from ATSPM 4.3 into the ATSPM 5.2 target. Use `--delete` to start from a clean slate.

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --delete
```

### 3. Migrate event logs

Choose a date range. Events are processed in one-hour windows and reruns are safe — existing windows are replaced rather than duplicated.

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

### 4. Migrate speed events (if applicable)

Use date-only values for start and end. The full end date is included automatically.

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --start 2024-01-01 `
  --end 2024-01-07
```

### 5. Or run all three steps at once

```powershell
dotnet run --project . -- upgrade-to-5-2 `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

## Quick Start (Summary)

```powershell
dotnet run --project . -- transfer-config --source "<sql-server-connection>"
dotnet run --project . -- transfer-events --source "<sql-server-connection>" --start 2024-01-01 --end 2024-01-07
dotnet run --project . -- upgrade-to-5-2 --source "<sql-server-connection>" --start 2024-01-01 --end 2024-01-07
```

## Documentation

- [Installation](docs/installation.md)
- [Configuration](docs/configuration.md)
- [Usage](docs/usage.md)
- [Upgrade Guide](docs/upgrade-guide.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Design](docs/design/data-migrator-extraction-plan.md)

