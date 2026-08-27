# Upgrade Guide

This is the recommended flow for moving from ATSPM 4.3 to an ATSPM 5 target with `DataMigrator`.

## Before You Start

- Confirm the source ATSPM 4.3 database is SQL Server.
- Confirm the target ATSPM 5 environment is reachable and configured.
- Confirm the provider and destination connection strings are set correctly.
- Confirm the source and target logins have the permissions in [Configuration and Permissions](configuration.md).
- Back up the target configuration database before any run that uses `--delete`.
- Decide the date range for event and speed migration.

## Recommended Flow

1. Validate the target configuration for the ATSPM 5 environment.
2. Back up the target, then start with a clean configuration replacement if required.
3. Run event-log migration for the required range.
4. Run speed-event migration for the required range if speed data is used.
5. Validate migrated locations, devices, and event data before cutover.

## Single-Command Path

For the standard path, use:

```powershell
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

## Step-By-Step Path

Configuration:

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --delete
```

Events:

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-07T23:59:59
```

Speed:

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01 `
  --end 2024-01-07
```

## Validation Checklist

- Confirm expected locations exist in the target.
- Confirm detectors and device configuration were imported correctly.
- Confirm event logs are present for the selected range.
- Confirm speed events are present if speed data is part of the deployment.
- Confirm the target application starts cleanly against the migrated data.

## Live Validation Workflow

Use this workflow before running a wide migration window.

### 1. Pick A Small Validation Slice

- Choose one known-good location identifier.
- Choose one event window that is small enough to inspect manually, such as one hour or one day.
- Choose one speed window for the same location if speed data is expected.

Example values used below:

- Location: `1234`
- Event window: `2024-01-01T00:00:00` through `2024-01-01T23:59:59`
- Speed window: `2024-01-01` through `2024-01-01`

### 2. Start With A Config Replacement

Run a clean configuration replacement before event or speed validation:

Do this only after backing up the target and confirming both source and target connection strings.

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --delete
```

Verify:

- The selected location exists in the target.
- The selected location has a signal-controller device.
- If speed validation is planned, the selected location also has a speed-sensor device.
- The target configuration reflects the source baseline you expect to validate against.

Why start this way:

- It removes uncertainty from older target-side configuration rows.
- It makes later event and speed reruns easier to interpret.
- It gives you a known baseline before validating replacement behavior for event and speed windows.

### 3. Run First Event Validation Load

```powershell
dotnet run --project . -- transfer-events `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T00:00:00 `
  --end 2024-01-01T23:59:59 `
  --locations 1234
```

Expect:

- Per-window processing messages.
- Insert batch messages.
- No repeated signal-controller warnings for the selected location.

Verify after the run:

- Event data exists for the selected location and time window.
- The loaded window boundaries match the requested range.

### 4. Re-Run The Same Event Window

Run the exact same command a second time.

Expect:

- A log message indicating existing event windows are being replaced before insert.
- No evidence that the rerun appended duplicate compressed windows for the same location and time span.

Verify after the rerun:

- The selected event windows still exist.
- The number of compressed windows for the selected location and time span does not grow after the rerun.

### 5. Run First Speed Validation Load

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01 `
  --end 2024-01-01 `
  --locations 1234
```

Expect:

- Per-window speed processing messages.
- Flush messages for compressed speed-event records.
- No repeated "No speed device found" warnings for the selected location.

Verify after the run:

- Speed-event data exists for the selected location and day.
- The loaded windows align to the requested date range.

### 6. Re-Run The Same Speed Window

Run the exact same speed command a second time.

Expect:

- A log message indicating existing speed-event windows are being replaced before insert.
- No growth in matching compressed speed windows after the rerun.

### 7. Expand Gradually

After one location and one window validate cleanly:

- Expand to a small set of locations.
- Expand to a multi-day range.
- Only then run the full migration window.
