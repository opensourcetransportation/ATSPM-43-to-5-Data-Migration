# Integration Testing

[`scripts/integration-test.ps1`](../scripts/integration-test.ps1) exercises the published Linux container against a real ATSPM 4.3 SQL Server source and a running Docker PostgreSQL target. It performs actual writes to the configured target; use a disposable development environment.

The script:

1. Checks Docker Desktop and the PostgreSQL container.
2. Builds the migration image.
3. Optionally runs the non-destructive incremental configuration migration.
4. Transfers one hour of controller events for a known location.
5. Transfers the same hour of speed events for a known speed location.
6. Runs the combined workflow with configuration skipped.
7. Repeats the combined workflow and verifies that each event type still has exactly one non-empty compressed window.

## Prerequisites

- Start the ATSPM Docker development stack. The default PostgreSQL container name is `atspm-postgres-1`.
- Configure `DatabaseConfiguration` target values using this project's .NET user secrets or environment variables.
- Set `MigrationCommandConfiguration:Source` to the ATSPM 4.3 SQL Server connection string. The script never prints secret values.
- Choose locations that contain controller and speed data during the test hour. The combined location must contain both types.
- Ensure the workstation can resolve and reach the source SQL Server. The script maps the workstation-resolved address into the container, which is useful with Docker Desktop and corporate VPN DNS.

## Run the Test

The defaults reproduce the checked integration slice from June 1, 2024, midnight through 1:00 AM. Configuration is skipped by default because it is not bounded by the one-hour range and can take substantially longer:

```powershell
./scripts/integration-test.ps1
```

Use another hour or known locations:

```powershell
./scripts/integration-test.ps1 `
  -Date 2024-06-01 `
  -Hour 0 `
  -EventLocation 6424 `
  -SpeedLocation 6702 `
  -CombinedLocation 6702
```

Include the non-destructive configuration command when validating all four commands:

```powershell
./scripts/integration-test.ps1 -IncludeConfiguration
```

After the first successful run, reuse the cached image for a faster verification:

```powershell
./scripts/integration-test.ps1 -SkipImageBuild
```

Other target layouts can override `-PostgresContainer`, `-ConfigDatabase`, `-EventLogDatabase`, and `-ImageTag`. Use `-SourceSecretKey` when the source connection is stored under a different configuration key.

## Safety and Expected Changes

The script never passes `--delete`. The incremental configuration phase can add or update target configuration, and the event phases replace compressed windows for the selected hour and locations. It does not clean those records afterward because leaving them in place is required to verify idempotent replacement.

The test fails if a command returns a nonzero exit code, if a required compressed window is absent or empty, or if a rerun leaves more than one matching compressed window.
