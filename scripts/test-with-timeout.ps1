param(
    [string]$Project = "tests\DataMigrator.Tests\DataMigrator.Tests.csproj",
    [int]$TimeoutSeconds = 120
)

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = "dotnet"
$runSettings = "tests\test.runsettings"
$startInfo.Arguments = "test $Project -v minimal --settings $runSettings"
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
# Enable short retry policy in tests
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
$process.Start() | Out-Null

$stdout = $process.StandardOutput
$stderr = $process.StandardError

$watch = [System.Diagnostics.Stopwatch]::StartNew()

while (-not $process.HasExited) {
    if ($watch.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
        Write-Host "Timeout reached ($TimeoutSeconds s). Killing test process..."
        try { $process.Kill() } catch { }
        break
    }
    Start-Sleep -Milliseconds 500
}

# drain output
while (-not $stdout.EndOfStream) { Write-Host $stdout.ReadLine() }
while (-not $stderr.EndOfStream) { Write-Host $stderr.ReadLine() }

if ($process.ExitCode -ne 0) {
    Write-Host "dotnet test exited with code $($process.ExitCode)"
    exit $process.ExitCode
} else {
    Write-Host "dotnet test completed successfully"
}
