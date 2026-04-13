# Usage

## Commands

- `transfer-config`
- `transfer-events`
- `transfer-speed`
- `upgrade-to-5-2`

## transfer-config

Moves configuration data from ATSPM 4.3 SQL Server into the configured ATSPM 5.2 target.

Use `--delete` when you want a clean configuration replacement before validation or before a fresh migration attempt.

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..."
```

Optional flags:

- `--delete`
- `--update-locations`
- `--update-speed`

## transfer-events

Moves controller event log data for a date range.

Rerunning the same location and time windows replaces previously loaded compressed event rows for those exact windows instead of appending duplicates.

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

Optional flags:

- `--batch`
- `--device`
- `--locations`

## transfer-speed

Moves speed event data for a date range.

Rerunning the same location and time windows replaces previously loaded compressed speed-event rows for those exact windows instead of appending duplicates.

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --start 2024-01-01 `
  --end 2024-01-07
```

## upgrade-to-5-2

Runs the standard upgrade sequence using one command.

```powershell
dotnet run --project . -- upgrade-to-5-2 `
  --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

Optional flags:

- `--delete`
- `--update-locations`
- `--update-speed`
- `--batch`
- `--device`
- `--locations`
- `--skip-config`
- `--skip-events`
- `--skip-speed`
