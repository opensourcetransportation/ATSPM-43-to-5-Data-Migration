# Contributing

Thank you for your interest in contributing to ATSPM-43-to-5-Data-Migration.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8)
- Access to an ATSPM 4.3 SQL Server source and an ATSPM 5 target for live testing
- The local NuGet package feed (see [Configuration](docs/configuration.md)) until UDOT packages are published to a public feed

## Building

```powershell
dotnet build DataMigrator.csproj -c Release
```

## Running Tests

```powershell
dotnet test DataMigrator.slnx -c Release
```

All 17+ tests should pass with no warnings before submitting a pull request.

## Running Locally

1. Configure target database settings under `DatabaseConfiguration` in `appsettings.json` or via user secrets.
2. Run any command directly:

```powershell
dotnet run --project . -- transfer-config --source "Server=...;Database=ATSPM;..."
```

See [Usage](docs/usage.md) for the full command reference.

## Making Changes

- Keep changes focused. One fix or feature per pull request.
- Follow the existing code style — no new comments or docstrings on unchanged code.
- Add or update tests for any changed behavior.
- Run `dotnet build` and `dotnet test` before submitting.
- If your change affects migration behavior, include live validation results in the PR description.

## Pull Requests

- Target the `main` branch.
- Fill out the pull request template.
- Reference any related issues with `Fixes #123` or `Closes #123`.

## Reporting Issues

Use the GitHub issue templates for bug reports and feature requests.

For security vulnerabilities, see [SECURITY.md](SECURITY.md).
