[CmdletBinding()]
param(
    [datetime]$Date = [datetime]"2024-06-01",
    [ValidateRange(0, 23)]
    [int]$Hour = 0,
    [ValidatePattern("^[A-Za-z0-9_-]+$")]
    [string]$EventLocation = "6424",
    [ValidatePattern("^[A-Za-z0-9_-]+$")]
    [string]$SpeedLocation = "6702",
    [ValidatePattern("^$|^[A-Za-z0-9_-]+$")]
    [string]$CombinedLocation = "",
    [ValidateRange(1, 600)]
    [int]$Batch = 100,
    [string]$PostgresContainer = "atspm-postgres-1",
    [string]$ConfigDatabase = "ATSPM-Config",
    [string]$EventLogDatabase = "ATSPM-EventLogs",
    [string]$ImageTag = "atspm-data-migrator:integration",
    [string]$SourceSecretKey = "MigrationCommandConfiguration:Source",
    [switch]$IncludeConfiguration,
    [switch]$SkipImageBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "DataMigrator.csproj"
$temporaryEnvironmentFile = $null

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Description,
        [Parameter(Mandatory)]
        [scriptblock]$Command
    )

    Write-Host "`n==> $Description"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-UserSecretMap {
    $lines = @(& dotnet user-secrets list --project $projectPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read user secrets for $projectPath."
    }

    $result = @{}
    foreach ($line in $lines) {
        $parts = [string]$line -split " = ", 2
        if ($parts.Count -eq 2) {
            $result[$parts[0]] = $parts[1]
        }
    }

    return $result
}

function Get-SourceConnectionString {
    param([hashtable]$Secrets)

    $environmentName = $SourceSecretKey -replace ":", "__"
    $source = [Environment]::GetEnvironmentVariable($environmentName)
    if ([string]::IsNullOrWhiteSpace($source) -and $Secrets.ContainsKey($SourceSecretKey)) {
        $source = [string]$Secrets[$SourceSecretKey]
    }

    if ([string]::IsNullOrWhiteSpace($source)) {
        throw "Set the $SourceSecretKey user secret or $environmentName environment variable."
    }

    return $source
}

function Get-TargetEnvironment {
    param([hashtable]$Secrets)

    $result = @{}
    foreach ($entry in $Secrets.GetEnumerator()) {
        if ($entry.Key -like "DatabaseConfiguration:*") {
            $result[$entry.Key -replace ":", "__"] = [string]$entry.Value
        }
    }

    foreach ($entry in Get-ChildItem Env:) {
        if ($entry.Name -like "DatabaseConfiguration__*") {
            $result[$entry.Name] = [string]$entry.Value
        }
    }

    if ($result.Count -eq 0) {
        throw "No DatabaseConfiguration target settings were found in user secrets or environment variables."
    }

    foreach ($key in @($result.Keys)) {
        if ($key -like "*__Host" -and $result[$key] -in @("localhost", "127.0.0.1")) {
            $result[$key] = "host.docker.internal"
        }
    }

    return $result
}

function Get-SourceDockerHostMapping {
    param([string]$ConnectionString)

    try {
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($ConnectionString)
        $dataSource = [string]$builder["Data Source"]
        if ([string]::IsNullOrWhiteSpace($dataSource)) {
            $dataSource = [string]$builder["Server"]
        }

        $hostName = (($dataSource -replace "^tcp:", "") -split "[,\\]")[0]
        if ([string]::IsNullOrWhiteSpace($hostName)) {
            return $null
        }

        $address = Resolve-DnsName -Name $hostName -Type A -ErrorAction Stop |
            Where-Object IPAddress |
            Select-Object -First 1 -ExpandProperty IPAddress
        if ($address) {
            return "${hostName}:${address}"
        }
    }
    catch {
        Write-Verbose "No explicit source host mapping was created: $($_.Exception.Message)"
    }

    return $null
}

function Invoke-Migrator {
    param(
        [string]$Description,
        [string[]]$Arguments
    )

    Invoke-CheckedCommand $Description {
        & docker @script:dockerRunArguments $ImageTag @Arguments
    }
}

function Invoke-PostgresQuery {
    param([string]$Query)

    $escapedQuery = $Query.Replace('"', '\"')
    $shellCommand = 'PGPASSWORD="$POSTGRES_PASSWORD" psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "' +
        $EventLogDatabase + '" -Atc "' + $escapedQuery + '"'
    $output = @(& docker exec $PostgresContainer sh -lc $shellCommand 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification failed: $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Assert-CompressedWindow {
    param(
        [string]$Location,
        [ValidateSet("IndianaEvent", "SpeedEvent")]
        [string]$DataType
    )

    $query = @"
select count(*), coalesce(sum(octet_length("Data")), 0)
from "CompressedEvents"
where "LocationIdentifier" = '$Location'
  and "DataType" = '$DataType'
  and "Start" >= '$script:startSql'
  and "Start" < '$script:endExclusiveSql';
"@
    $result = @(Invoke-PostgresQuery $query) | Select-Object -Last 1
    $values = $result -split "\|", 2
    if ($values.Count -ne 2 -or [int]$values[0] -ne 1 -or [long]$values[1] -le 0) {
        throw "Expected one non-empty $DataType window for location $Location, but PostgreSQL returned '$result'."
    }

    Write-Host "Verified $DataType for ${Location}: rows=$($values[0]), compressedBytes=$($values[1])"
}

try {
    if ($CombinedLocation -eq "") {
        $CombinedLocation = $SpeedLocation
    }

    foreach ($databaseName in @($ConfigDatabase, $EventLogDatabase)) {
        if ($databaseName -notmatch "^[A-Za-z0-9_-]+$") {
            throw "Database name '$databaseName' contains unsupported characters."
        }
    }

    Push-Location $repositoryRoot
    Invoke-CheckedCommand "Check Docker Desktop" { & docker version --format "{{.Server.Version}}" }
    Invoke-CheckedCommand "Check PostgreSQL container" { & docker inspect --format "{{.State.Running}}" $PostgresContainer }

    $secrets = Get-UserSecretMap
    $sourceConnectionString = Get-SourceConnectionString $secrets
    $targetEnvironment = Get-TargetEnvironment $secrets
    $targetEnvironment["DatabaseConfiguration__ConfigContext__Database"] = $ConfigDatabase
    $targetEnvironment["DatabaseConfiguration__EventLogContext__Database"] = $EventLogDatabase

    $temporaryEnvironmentFile = [IO.Path]::GetTempFileName()
    $environmentLines = foreach ($entry in $targetEnvironment.GetEnumerator()) {
        "$($entry.Key)=$($entry.Value)"
    }
    [IO.File]::WriteAllLines($temporaryEnvironmentFile, $environmentLines)

    if (-not $SkipImageBuild) {
        Invoke-CheckedCommand "Build migration image" { & docker build --tag $ImageTag . }
    }

    $script:dockerRunArguments = @(
        "run",
        "--rm",
        "--env-file", $temporaryEnvironmentFile,
        "--add-host", "host.docker.internal:host-gateway"
    )
    $sourceHostMapping = Get-SourceDockerHostMapping $sourceConnectionString
    if ($sourceHostMapping) {
        $script:dockerRunArguments += @("--add-host", $sourceHostMapping)
    }

    $start = $Date.Date.AddHours($Hour)
    $endExclusive = $start.AddHours(1)
    $inclusiveEnd = $endExclusive.AddTicks(-1)
    $startArgument = $start.ToString("yyyy-MM-ddTHH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)
    $endArgument = $inclusiveEnd.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", [Globalization.CultureInfo]::InvariantCulture)
    $script:startSql = $start.ToString("yyyy-MM-dd HH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)
    $script:endExclusiveSql = $endExclusive.ToString("yyyy-MM-dd HH:mm:ss", [Globalization.CultureInfo]::InvariantCulture)

    Write-Host "Testing one-hour window $startArgument through $endArgument"

    if ($IncludeConfiguration) {
        Invoke-Migrator "Run incremental configuration migration" @(
            "transfer-config", "--source", $sourceConnectionString
        )
    }

    Invoke-Migrator "Run controller-event migration for $EventLocation" @(
        "transfer-events", "--source", $sourceConnectionString,
        "--start", $startArgument, "--end", $endArgument,
        "--locations", $EventLocation, "--batch", "$Batch"
    )
    Assert-CompressedWindow $EventLocation "IndianaEvent"

    Invoke-Migrator "Run speed-event migration for $SpeedLocation" @(
        "transfer-speed", "--source", $sourceConnectionString,
        "--start", $startArgument, "--end", $endArgument,
        "--locations", $SpeedLocation
    )
    Assert-CompressedWindow $SpeedLocation "SpeedEvent"

    $combinedArguments = @(
        "upgrade-to-5", "--source", $sourceConnectionString,
        "--start", $startArgument, "--end", $endArgument,
        "--locations", $CombinedLocation, "--batch", "$Batch", "--skip-config"
    )
    Invoke-Migrator "Run combined migration for $CombinedLocation" $combinedArguments
    Assert-CompressedWindow $CombinedLocation "IndianaEvent"
    Assert-CompressedWindow $CombinedLocation "SpeedEvent"

    Invoke-Migrator "Rerun combined migration to verify idempotency" $combinedArguments
    Assert-CompressedWindow $CombinedLocation "IndianaEvent"
    Assert-CompressedWindow $CombinedLocation "SpeedEvent"

    Write-Host "`nIntegration test passed. All commands transferred data and the combined rerun created no duplicates."
}
finally {
    if ($temporaryEnvironmentFile -and (Test-Path -LiteralPath $temporaryEnvironmentFile)) {
        Remove-Item -LiteralPath $temporaryEnvironmentFile -Force
    }
    if ((Get-Location).Path -eq $repositoryRoot) {
        Pop-Location
    }
}
