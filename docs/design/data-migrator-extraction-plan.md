# DataMigrator Extraction Plan

## Summary

This repository is the extracted `.NET 8` migration utility for moving ATSPM 4.3 data into an ATSPM 5.2 environment.

Core product rules:

- Source is always SQL Server.
- Target provider is configuration-driven.
- Supported commands are `transfer-config`, `transfer-events`, `transfer-speed`, and `upgrade-to-5-2`.
- IIS-oriented documentation uses `C:\inetpub` as the default IIS path.

## Current Status

### Completed

- Created a standalone root-level `.NET 8` solution around [DataMigrator.csproj](C:\Projects\ATSPM-43-to-5-Data-Migration\DataMigrator.csproj).
- Reduced the command surface to:
  - `transfer-config`
  - `transfer-events`
  - `transfer-speed`
  - `upgrade-to-5-2`
- Removed unrelated `DatabaseInstaller` commands from the extracted tool.
- Replaced source project coupling with NuGet package references.
- Added a temporary local NuGet feed workaround through [nuget.config](C:\Projects\ATSPM-43-to-5-Data-Migration\nuget.config) and `local-packages` because the published UDOT package set is incomplete on the online feeds.
- Flattened the repo layout so the app lives at the repository root instead of under `src/`.
- Added release automation scaffolding under [.github/workflows](C:\Projects\ATSPM-43-to-5-Data-Migration\.github\workflows) for:
  - Windows executable packaging
  - container image publishing
- Added documentation structure under [docs](C:\Projects\ATSPM-43-to-5-Data-Migration\docs).
- Added starter test coverage under [tests/DataMigrator.Tests](C:\Projects\ATSPM-43-to-5-Data-Migration\tests\DataMigrator.Tests).

### Verified

- Re-verified locally on 2026-04-13: `dotnet build .\DataMigrator.csproj -c Release` succeeds.
- Re-verified locally on 2026-04-10: `dotnet test .\DataMigrator.slnx -c Release` passes with 17 tests.
- Configuration migration was run successfully against the configured PostgreSQL target with `transfer-config --delete`.
- Device configuration descriptions were validated in PostgreSQL and did not collapse to the first description.
- Active current locations, using the same repository-style selection logic as the app, now all have signal-controller devices after the config import fix.
- Live event validation succeeded for location `7365` for `2025-03-18 10:00:00` through `10:59:59` after loading current locations with devices explicitly.
- Live event rerun validation also succeeded for the same `7365` hour, and the replacement log confirmed existing compressed windows were replaced rather than duplicated.
- Live target config validation confirmed current active location `7365` has both a signal-controller device and a speed-sensor device attached.
- Live target config validation confirmed current active location `5234` has both a signal-controller device and a speed-sensor device attached.
- The `transfer-speed` command now honors `--locations` correctly.

### In Progress

- Speed migration now uses the same latest active location selection model as event migration so old data is resolved against the current migrated device assignments. This change is implemented in [SpeedEventMigrationService.cs](C:\Projects\ATSPM-43-to-5-Data-Migration\Services\SpeedEventMigrationService.cs), and it has now been live-validated for both first load and rerun-safe replacement for location `5234` for `2026-04-10 09:00:00` through `09:59:59`.
- No local build or test blockers are currently known in the extracted tool.

## Important Fixes Made

### Controller Device Relinking

Controller devices are now relinked during config import to the target's latest location version by `DeviceIdentifier` instead of trusting the source `LocationId` directly.

Why this was needed:

- Some identifiers have multiple active location rows with the same latest `Start`.
- The original import could attach the controller device to one sibling version row while other same-start rows appeared to have no device.
- The relink logic now aligns imported controllers with the repository-selected current location behavior used by the application.

### Event Location Selection

Event migration was changed to use the latest active locations rather than `GetLatestVersionOfAllLocations(date)`.

Why this was needed:

- For older event dates, historical location versions may not have a controller device attached in the migrated config.
- The desired migration behavior for this tool is to use the latest version of the locations for device resolution.

### Speed Location Selection

Speed migration now follows the same latest active location selection strategy as event migration instead of resolving locations by the historical hour being imported.

Why this was needed:

- Older speed-event windows can hit the same version-skew problem as controller events.
- The migration tool should resolve devices against the imported current configuration rather than historical location-version snapshots.

### Event Rerun Replacement

Event reruns now replace matching compressed windows using stable keys instead of attempting to append a second copy of the same window.

Why this was needed:

- Re-running the same live hour originally hit duplicate-key violations in the target event-log store.
- Matching on `(LocationIdentifier, DeviceId, Start)` proved more stable than requiring exact `End` equality across reruns.
- Inclusive end normalization was also adjusted so whole-second event end times expand consistently to the same exclusive boundary.

### Speed Command Location Filtering

The speed command now exposes and binds `--locations` correctly.

Why this was needed:

- The migration service already supported location filtering, but the command surface did not pass the option through.
- Early live speed validation accidentally queried unrelated locations because the location restriction was ignored.

### Speed Source Query Diagnostics

The speed migration query path was tightened during live validation with:

- explicit SQL parameter types
- a `ByTimestampByDetID` index hint
- a longer source timeout
- targeted diagnostic logging around location loading and source query execution
- a switch to a normalized `Microsoft.Data.SqlClient` source connection built with explicit `Pooling=false`, `MultipleActiveResultSets=false`, and `ApplicationName` settings
- `CommandBehavior.SingleResult` on the source reader
- `OPTION (RECOMPILE)` on the speed source query

Why this was needed:

- Direct ad hoc source probes for location `5234` were fast, but the live migration path still needed explicit connection and execution settings to behave consistently against the SQL Server source.
- The final live validation succeeded after normalizing the source connection string and forcing the source command into a single-result, recompiled execution path.

## Validation Findings

### Configuration Validation

Validated directly in PostgreSQL:

- `DeviceConfigurations.Description` values are distinct and correct.
- `Locations` were imported.
- `Devices` were imported.
- No imported devices were missing `LocationId`.
- Signal-controller devices are present for all repository-selected current locations.

### Event Validation

Validated live against the configured target:

- Location `7365` initially failed because the migration service was loading current locations without `Devices`, not because config transfer missed the device.
- After explicitly loading devices in the event service, a one-hour live event migration for `7365` completed successfully.
- Re-running the same hour replaced the existing compressed event window successfully.
- The event path is now considered live-validated for both first load and rerun replacement behavior.

### Speed Validation

Validated live against the configured source and target:

- Direct ad hoc SQL probes against the `MOE` source for location `5234` for `2026-04-10 09:00:00` through `09:59:59` returned 672 matching rows quickly, confirming the source data path and detector set were valid.
- After normalizing the source connection string and hardening the source read path, `transfer-speed` completed successfully for location `5234` for `2026-04-10 09:00:00` through `09:59:59`.
- The live run loaded 672 source speed events, replaced 1 existing compressed speed-event window, and flushed 1 compressed speed-event record.
- Re-running the same hour again logged the same replacement behavior, and direct PostgreSQL validation confirmed the target still contained exactly 1 `SpeedEvent` compressed row for `LocationIdentifier = '5234'` and `Start = 2026-04-10 09:00:00`.
- The speed path is now considered live-validated for both first load and rerun replacement behavior.

### Package Feed Validation

The online feeds alone are not currently sufficient for clean restore.

Missing online packages include:

- `Utah.Udot.Atspm.Data`
- `Utah.Udot.Atspm.MySqlDatabaseProvider`
- `Utah.Udot.Atspm.OracleDatabaseProvider`
- `Utah.Udot.Atspm.PostgreSQLDatabaseProvider`
- `Utah.Udot.Atspm.SqlDatabaseProvider`
- `Utah.Udot.Atspm.SqlLiteDatabaseProvider`

This is why the repository currently depends on the local package feed workaround.

## Remaining Work

### High Priority

- Add targeted automated coverage around speed migration source-connection normalization and rerun replacement behavior so this live fix stays protected.
- Confirm whether any additional narrow-slice live validation is still needed beyond the now-verified `5234` hour baseline.

### Medium Priority

- Improve operator-facing troubleshooting for long-running speed imports and duplicate/rerun handling.
- Add targeted tests for migration orchestration edge cases beyond command and hosted-service coverage.
- Confirm release workflows match the Open Source Transportation ATSPM release conventions end to end.

### Later

- Remove the local NuGet feed workaround once missing UDOT packages are published to a shared online feed.
- Consider adding import-time metrics or summaries for migrated counts by entity type and date range.

## Next Recommended Steps

1. Add focused automated tests around `SpeedEventMigrationService` for source-connection normalization and rerun-safe replacement by `(LocationIdentifier, DeviceId, Start)`.
2. If operators still see intermittent SQL Server variability in the field, capture the exact command line and compare it against the now-validated `5234` hour baseline before changing the query shape again.
3. Commit the repo state as the current live-validated migration baseline.

