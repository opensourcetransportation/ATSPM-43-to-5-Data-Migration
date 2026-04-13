# Installation

## Windows / EXE

Release builds publish a self-contained Windows executable. No .NET SDK is required on the target machine.

### Steps

1. Go to the [GitHub Releases](../../releases) page and download the latest `ATSPM-43-to-5-Data-Migration-win-x64-<version>.zip`.

2. Extract the ZIP to a working directory, for example:

   ```
   C:\inetpub\DataMigrator\
   ```

3. Open `appsettings.json` in that directory and configure the ATSPM 5.2 target connection strings. See [Configuration](configuration.md) for details.

4. Run the tool from a command prompt or PowerShell:

   ```powershell
   .\DataMigrator.exe transfer-config --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..."
   ```

   Replace the source connection string with your ATSPM 4.3 SQL Server details.

5. For sensitive environments, use environment variables or [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) instead of storing credentials in `appsettings.json`.

---

## Container

Release builds publish a container image to the GitHub Container Registry (GHCR).

The image is published at:

```
ghcr.io/udot-utah/atspm-43-to-5-data-migration:<version>
```

### Steps

1. Pull the image:

   ```bash
   docker pull ghcr.io/udot-utah/atspm-43-to-5-data-migration:latest
   ```

2. Mount a local `appsettings.json` with your target connection strings and run the desired command:

   ```bash
   docker run --rm \
     -v "$(pwd)/appsettings.json:/app/appsettings.json" \
     ghcr.io/udot-utah/atspm-43-to-5-data-migration:latest \
     transfer-config --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..."
   ```

3. Alternatively, pass connection strings as environment variables using the standard .NET configuration override format:

   ```bash
   docker run --rm \
     -e "ConnectionStrings__ConfigContext__Provider=PostgreSql" \
     -e "ConnectionStrings__ConfigContext__ConnectionString=Host=db01;..." \
     ghcr.io/udot-utah/atspm-43-to-5-data-migration:latest \
     transfer-config --source "Server=sql01;Database=ATSPM;User Id=sa;Password=..."
   ```

---

## Building From Source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8).

```powershell
git clone https://github.com/udot-utah/ATSPM-43-to-5-Data-Migration.git
cd ATSPM-43-to-5-Data-Migration
dotnet build
dotnet run --project . -- transfer-config --source "..."
```

See [Configuration](configuration.md) before running to set up the target connection strings.
