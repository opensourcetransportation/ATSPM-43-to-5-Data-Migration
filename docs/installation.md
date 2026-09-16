# Installation

Choose the Windows executable, container image, or source-build path. In every case, prepare the target settings described in [Configuration and Permissions](configuration.md) before starting a migration.

## Windows Executable

Release builds contain a self-contained Windows x64 executable, so the target machine does not need the .NET SDK.

1. Open the repository's [GitHub Releases](https://github.com/opensourcetransportation/ATSPM-43-to-5-Data-Migration/releases) page.
2. Download `ATSPM-43-to-5-Data-Migration-win-x64-<version>.zip` from the desired release.
3. Extract the ZIP into a dedicated directory, such as `C:\Tools\DataMigrator`.
4. Configure `appsettings.json` in the extracted application directory.
5. Confirm the executable and configuration are working:

   ```powershell
   .\DataMigrator.exe --help
   .\DataMigrator.exe transfer-config --help
   ```

6. Start with a non-destructive configuration import or a narrow event slice:

   ```powershell
   .\DataMigrator.exe transfer-events `
     --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." `
     --start 2024-01-01T09:00:00 `
     --end 2024-01-01T09:59:59 `
     --locations 1234
   ```

Run commands from the extracted application directory so the bundled `appsettings.json` is found.

## Container

Release builds publish to the repository-derived GitHub Container Registry path:

```text
ghcr.io/opensourcetransportation/atspm-43-to-5-data-migration:<tag>
```

Stable releases also publish `latest`; version tags normally include the leading `v` from the GitHub release tag.

```bash
docker pull ghcr.io/opensourcetransportation/atspm-43-to-5-data-migration:latest
```

Mount a complete settings file read-only:

```bash
docker run --rm \
  -v "$(pwd)/appsettings.json:/app/appsettings.json:ro" \
  ghcr.io/opensourcetransportation/atspm-43-to-5-data-migration:latest \
  transfer-config \
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..."
```

Or supply the required target contexts as environment variables:

```bash
docker run --rm \
  -e "DatabaseConfiguration__ConfigContext__DBType=PostgreSql" \
  -e "DatabaseConfiguration__ConfigContext__Host=db01" \
  -e "DatabaseConfiguration__ConfigContext__Database=atspm" \
  -e "DatabaseConfiguration__ConfigContext__User=..." \
  -e "DatabaseConfiguration__ConfigContext__Password=..." \
  -e "DatabaseConfiguration__EventLogContext__DBType=PostgreSql" \
  -e "DatabaseConfiguration__EventLogContext__Host=db01" \
  -e "DatabaseConfiguration__EventLogContext__Database=atspm" \
  -e "DatabaseConfiguration__EventLogContext__User=..." \
  -e "DatabaseConfiguration__EventLogContext__Password=..." \
  -e "DatabaseConfiguration__AggregationContext__DBType=PostgreSql" \
  -e "DatabaseConfiguration__AggregationContext__Host=db01" \
  -e "DatabaseConfiguration__AggregationContext__Database=atspm" \
  -e "DatabaseConfiguration__AggregationContext__User=..." \
  -e "DatabaseConfiguration__AggregationContext__Password=..." \
  -e "DatabaseConfiguration__IdentityContext__DBType=PostgreSql" \
  -e "DatabaseConfiguration__IdentityContext__Host=db01" \
  -e "DatabaseConfiguration__IdentityContext__Database=atspm" \
  -e "DatabaseConfiguration__IdentityContext__User=..." \
  -e "DatabaseConfiguration__IdentityContext__Password=..." \
  ghcr.io/opensourcetransportation/atspm-43-to-5-data-migration:latest \
  transfer-events \
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..." \
  --start 2024-01-01T09:00:00 \
  --end 2024-01-01T09:59:59 \
  --locations 1234
```

The container must be able to resolve and reach both database hosts. Do not use `localhost` for a database running on the host unless the container runtime is configured to route it appropriately.

## Build From Source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8), then run:

```powershell
git clone https://github.com/opensourcetransportation/ATSPM-43-to-5-Data-Migration.git
cd ATSPM-43-to-5-Data-Migration
dotnet restore
dotnet build
dotnet run --project . -- --help
```

To run a command from source:

```powershell
dotnet run --project . -- transfer-config `
  --source "Server=sql01;Database=ATSPM43;User Id=...;Password=..."
```

## Installation Check

Before a production run, verify:

- `--help` starts without an assembly or runtime error.
- The source SQL Server is reachable from the execution environment.
- The configured target contexts use the expected provider and database.
- The database logins have the permissions listed in [Configuration and Permissions](configuration.md).
- A target backup exists before any `--delete` run.
