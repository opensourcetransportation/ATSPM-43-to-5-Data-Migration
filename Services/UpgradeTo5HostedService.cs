#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/UpgradeTo5HostedService.cs
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using DataMigrator.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace DataMigrator.Services;

public sealed class UpgradeTo5HostedService : IHostedService
{
    private readonly ILogger<UpgradeTo5HostedService> _logger;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IConfigurationMigrationService? _configurationMigrationService;
    private readonly IEventLogMigrationService? _eventLogMigrationService;
    private readonly ISpeedEventMigrationService? _speedEventMigrationService;
    private readonly UpgradeTo5CommandConfiguration _options;

    public UpgradeTo5HostedService(
        ILogger<UpgradeTo5HostedService> logger,
        IServiceScopeFactory scopeFactory,
        IOptions<UpgradeTo5CommandConfiguration> options)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    internal UpgradeTo5HostedService(
        ILogger<UpgradeTo5HostedService> logger,
        IConfigurationMigrationService configurationMigrationService,
        IEventLogMigrationService eventLogMigrationService,
        ISpeedEventMigrationService speedEventMigrationService,
        IOptions<UpgradeTo5CommandConfiguration> options)
    {
        _logger = logger;
        _configurationMigrationService = configurationMigrationService;
        _eventLogMigrationService = eventLogMigrationService;
        _speedEventMigrationService = speedEventMigrationService;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        var overallStopwatch = Stopwatch.StartNew();
        using var scope = _scopeFactory?.CreateScope();
        var configurationMigrationService = _configurationMigrationService ?? scope!.ServiceProvider.GetRequiredService<IConfigurationMigrationService>();
        var eventLogMigrationService = _eventLogMigrationService ?? scope!.ServiceProvider.GetRequiredService<IEventLogMigrationService>();
        var speedEventMigrationService = _speedEventMigrationService ?? scope!.ServiceProvider.GetRequiredService<ISpeedEventMigrationService>();

        if (!_options.SkipConfig)
        {
            await RunPhaseAsync(
                phaseName: "configuration migration",
                action: token => configurationMigrationService.RunAsync(new TransferConfigCommandConfiguration
                {
                    Source = _options.Source,
                    Delete = _options.Delete,
                    UpdateLocations = _options.UpdateLocations,
                    ImportSpeedDevices = _options.ImportSpeedDevices
                }, token),
                cancellationToken);
        }

        if (!_options.SkipEvents)
        {
            await RunPhaseAsync(
                phaseName: "event log migration",
                action: token => eventLogMigrationService.RunAsync(new MigrationCommandConfiguration
                {
                    Source = _options.Source,
                    Start = _options.Start,
                    End = _options.End,
                    EndIsDateOnly = _options.EndIsDateOnly,
                    Batch = _options.Batch,
                    Device = _options.Device,
                    Locations = _options.Locations
                }, token),
                cancellationToken);
        }

        if (!_options.SkipSpeed)
        {
            await RunPhaseAsync(
                phaseName: "speed event migration",
                action: token => speedEventMigrationService.RunAsync(new MigrationCommandConfiguration
                {
                    Source = _options.Source,
                    Start = _options.Start,
                    End = _options.End,
                    EndIsDateOnly = _options.EndIsDateOnly,
                    Locations = _options.Locations
                }, token),
                cancellationToken);
        }

        overallStopwatch.Stop();
        _logger.LogInformation(
            "Upgrade-to-5 orchestration completed in {ElapsedMs} ms. SkipConfig={SkipConfig}, SkipEvents={SkipEvents}, SkipSpeed={SkipSpeed}.",
            overallStopwatch.ElapsedMilliseconds,
            _options.SkipConfig,
            _options.SkipEvents,
            _options.SkipSpeed);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateOptions()
    {
        if (_options.SkipConfig && _options.SkipEvents && _options.SkipSpeed)
        {
            throw new InvalidOperationException("At least one migration phase must be enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.Source))
        {
            throw new InvalidOperationException(
                "A source connection string is required through --source or UpgradeTo5CommandConfiguration:Source.");
        }

        if ((!_options.SkipEvents || !_options.SkipSpeed) && (_options.Start == default || _options.End == default))
        {
            throw new InvalidOperationException(
                "Start and end are required for event or speed migration through command-line options or UpgradeTo5CommandConfiguration.");
        }
    }

    private async Task RunPhaseAsync(string phaseName, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting {PhaseName}.", phaseName);

        try
        {
            await action(cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation("Completed {PhaseName} in {ElapsedMs} ms.", phaseName, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "{PhaseName} failed after {ElapsedMs} ms.", phaseName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
