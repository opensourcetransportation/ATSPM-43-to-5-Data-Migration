# DataMigrator Extraction Plan

## Summary

This repository is the extracted `.NET 8` migration utility for moving ATSPM 4.3 data into a configured ATSPM 5 environment.

Core product rules:

- Source is always SQL Server.
- Target provider is configuration-driven.
- Supported commands are `transfer-config`, `transfer-events`, `transfer-speed`, and `upgrade-to-5`.
- Installation documentation uses a neutral dedicated tools directory rather than assuming IIS deployment.

## Current Status

### Completed

- Import-time metrics logged by `SpeedEventMigrationService` after each run: total elapsed time, hours processed, location reads, source events loaded, and compressed windows written.
- Automated tests added for speed migration source-connection normalization (`BuildSourceConnectionString`) and rerun-safe key matching (`GetExistingLogsAsync`) in `SpeedEventMigrationServiceTests`.
- Automated tests added for migration orchestration edge cases: all skip-flag combinations and failure propagation for both config and event phases.
- Operator-facing troubleshooting documentation expanded to cover long-running speed imports, rerun replacement behavior, duplicate detection queries, source gap comparison, and upgrade command skip flags.
- Created a standalone root-level `.NET 8` solution around [DataMigrator.csproj](../../DataMigrator.csproj).
- Reduced the command surface to:
  - `transfer-config`
  - `transfer-events`
  - `transfer-speed`
  - `upgrade-to-5`
- Removed unrelated `DatabaseInstaller` commands from the extracted tool.
- Replaced source project coupling with NuGet package references.
- Switched UDOT package references to the republished online package set, later advancing to the stable `5.3.1` packages, and removed the temporary local NuGet feed workaround.
- Added PostgreSQL target schema migration handling for the republished UDOT package model, including the official `20260521163837_5_3` config migration bridge needed by the current package metadata.
- Flattened the repo layout so the app lives at the repository root instead of under `src/`.
- Added release automation scaffolding under [.github/workflows](../../.github/workflows) for:
  - Windows executable packaging
  - container image publishing
- Added documentation structure under [docs](..).
- Added starter test coverage under [tests/DataMigrator.Tests](../../tests/DataMigrator.Tests).

### Verified

- Re-verified locally on 2026-08-27: all 98 automated tests pass against the `5.3.1` package baseline.
- Re-verified locally on 2026-04-13: `dotnet build .\DataMigrator.csproj -c Release` succeeds.
- Re-verified locally on 2026-04-10: `dotnet test .\DataMigrator.slnx -c Release` passes with 17 tests.
- Re-verified locally on 2026-05-29: `dotnet test .\DataMigrator.slnx -c Release` passes with 24 tests.
- Configuration migration was run successfully against the configured PostgreSQL target with `transfer-config --delete`.
- Device configuration descriptions were validated in PostgreSQL and did not collapse to the first description.
- Active current locations, using the same repository-style selection logic as the app, now all have signal-controller devices after the config import fix.
- Live event validation succeeded for location `7365` for `2025-03-18 10:00:00` through `10:59:59` after loading current locations with devices explicitly.
- Live event rerun validation also succeeded for the same `7365` hour, and the replacement log confirmed existing compressed windows were replaced rather than duplicated.
- Live target config validation confirmed current active location `7365` has both a signal-controller device and a speed-sensor device attached.
- Live target config validation confirmed current active location `5234` has both a signal-controller device and a speed-sensor device attached.
- The `transfer-speed` command now honors `--locations` correctly.
- Speed migration uses the same latest active location selection model as event migration so old data is resolved against the current migrated device assignments. Live-validated for both first load and rerun-safe replacement for location `5234` for `2026-04-10 09:00:00` through `09:59:59`.

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

The online feeds now restore `Utah.Udot.Atspm` and `Utah.Udot.Atspm.Infrastructure` `5.3.1`.

The local package feed workaround is no longer required.

For PostgreSQL targets, the migrator applies registered UDOT `ConfigContext` migrations and an idempotent bridge for the official `20260521163837_5_3` config migration before importing configuration data.

## Remaining Work

### Later

- Re-run live migration validation when adopting future ATSPM package releases.

## Next Recommended Steps

1. Add focused automated tests around `SpeedEventMigrationService` for source-connection normalization and rerun-safe replacement by `(LocationIdentifier, DeviceId, Start)`.
2. If operators still see intermittent SQL Server variability in the field, capture the exact command line and compare it against the now-validated `5234` hour baseline before changing the query shape again.
