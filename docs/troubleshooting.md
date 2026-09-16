# Troubleshooting

## Start With The Basics

Before digging into migration behavior:

- Verify the `--source` SQL Server connection string points at the expected ATSPM 4.3 database.
- Verify all target `DatabaseConfiguration` context values and provider settings for the ATSPM 5 environment.
- Confirm the selected date range and optional `--locations` filter match the slice you intend to migrate.
- Prefer a narrow validation slice first, such as one location for one hour, before retrying a wide migration window.

## Long-Running Speed Imports

Speed migration processes data in hourly windows and can take a long time on wide ranges or all-location runs.

What to look for in the logs:

- `Processing speed events from <start> to <end>` means the next hourly window has started.
- `Loaded <count> speed-migration locations for the current window.` shows how many locations are being evaluated for that hour.
- `Querying source speed events for <location> ...` indicates the source-side read is in progress for that location.
- `Loaded <count> source speed events for <location> ...` confirms that source rows were returned for that location and hour.
- `Flushed <count> compressed speed-event records.` confirms a batch was written to the target.
- `Speed-event migration completed in <ms> ...` is the end-of-run summary for the whole command.

If the run appears stuck:

- Reduce the range to one hour and retry.
- Limit the run to one or a few `--locations`.
- If `upgrade-to-5` is being used for a speed-only retry, add `--skip-config --skip-events`.
- Compare the same narrow slice directly against the source before assuming the target write path is broken.

Example narrow speed retry:

```powershell
dotnet run --project . -- transfer-speed `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T09:00:00 `
  --end 2024-01-01T09:59:59 `
  --locations 1234
```

## Understanding Reruns And Replacement

Event and speed reruns are intended to replace matching compressed windows rather than append duplicates.

What replacement means:

- Matching compressed windows are identified by stable keys.
- For both event and speed reruns, matching on `(LocationIdentifier, DeviceId, Start)` is more important than exact `End` equality.
- A rerun of the same logical window should keep the number of compressed rows stable for that location/device/start combination.

What to look for in the logs:

- `Replacing <count> existing event windows before insert ...`
- `Replacing <count> existing speed-event windows before insert ...`

If you do not see replacement logs on a rerun:

- Confirm you are rerunning the same effective window boundaries.
- Confirm the rerun is targeting the same `--locations` set.
- Confirm the same device mapping is still present in the target configuration.

## Checking For Duplicate Or Missing Rerun Results

If a rerun seems wrong, inspect the target compressed-event store directly.

For speed-event windows in PostgreSQL:

```sql
select "LocationIdentifier", "DeviceId", "Start", "End", "DataType", count(*)
from public."CompressedEvents"
where "DataType" = 'SpeedEvent'
  and "LocationIdentifier" = '1234'
  and "Start" = timestamp '2024-01-01 09:00:00'
group by "LocationIdentifier", "DeviceId", "Start", "End", "DataType";
```

Interpretation:

- `1` row for the expected `LocationIdentifier` and `Start` means the rerun replaced the prior compressed window instead of doubling it.
- More than `1` row for the same logical window means the rerun behavior needs investigation.
- `0` rows means either the source had no data, the location mapping was missing, or the run did not actually process that window.

## Comparing Target Gaps To Source Gaps

A missing target window does not automatically mean the migration skipped data. The source may also be empty for that location and hour.

Recommended process:

1. Identify a missing target location/hour pair.
2. Query the source `Speed_Events` table for the same hour using the detectors mapped to that location.
3. If the source is empty, the target gap is expected.
4. If the source has rows but the target does not, treat that as a migration gap worth escalating.

## Upgrade Command Troubleshooting

`upgrade-to-5` runs configuration, event, and speed phases in sequence unless skip flags are used.

Useful flags:

- `--skip-config`
- `--skip-events`
- `--skip-speed`

What to expect:

- The logs should show `Starting configuration migration.`, `Starting event log migration.`, or `Starting speed event migration.` only for phases that are actually enabled.
- The orchestration summary logs the effective skip values at the end of the run.

Example speed-only orchestration retry:

```powershell
dotnet run --project . -- upgrade-to-5 `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
  --start 2024-01-01T09:00:00 `
  --end 2024-01-01T09:59:59 `
  --skip-config `
  --skip-events
```

## Source Query Timeouts

The configured command timeouts are 300 seconds for configuration reads, 120 seconds for controller-event reads, and 300 seconds for speed-event reads. These are intentionally longer than the SQL client default.

If a query still times out:

- Check SQL Server blocking and resource pressure while the migration is running.
- Confirm the time and location filters are selective where the command supports them.
- Test a single location and one-hour window for event or speed data.
- Review indexes on event timestamps, signal/location identifiers, detector identifiers, and the `Start` columns used by the configuration queries.
- Run the corresponding source query in SQL Server Management Studio and capture its actual execution plan.

Configuration queries are defined in `appsettings.json` under `LocationQueries`. Avoid increasing timeouts repeatedly without checking the correlated latest-location subqueries and their indexes.

## Permission and Schema Errors

- A source `SELECT permission denied` error means the SQL Server login lacks access to one or more 4.3 tables listed in [Configuration and Permissions](configuration.md).
- A target insert/update error usually means the target login lacks DML permission or the target schema does not match the configured provider package.
- PostgreSQL errors involving migrations or `ALTER TABLE` require a login with the DDL/ownership permissions described in [Configuration and Permissions](configuration.md).
- Other target providers must have the target schema applied before migration; this tool only performs automatic schema compatibility work for PostgreSQL configuration targets.

## When To Escalate

Escalate with logs and exact command lines when:

- a narrow one-location, one-hour speed run repeatedly hangs or times out
- the source clearly has rows for a location/hour but the target does not
- rerunning the same window creates duplicate compressed rows instead of replacing the prior window
- `upgrade-to-5` starts a phase that was explicitly skipped

Include these details in the escalation:

- exact command used
- source database name
- target provider and database
- location identifiers used
- start/end values used
- relevant replacement or summary log lines
