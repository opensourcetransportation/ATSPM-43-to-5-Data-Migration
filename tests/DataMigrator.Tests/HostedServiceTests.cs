#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Tests/HostedServiceTests.cs
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
using DataMigrator.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataMigrator.Tests;

public sealed class HostedServiceTests
{
    [Fact]
    public async Task TransferConfigHostedService_ResolvesScopedMigrationServiceWithinScope()
    {
        var recorder = new RecordingConfigurationMigrationService();
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new TransferConfigCommandConfiguration { Source = "source" }));
        services.AddScoped<IConfigurationMigrationService>(_ => recorder);
        services.AddHostedService<TransferConfigCommandHostedService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var hostedService = provider.GetServices<IHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);

        Assert.NotNull(recorder.LastConfig);
        Assert.Equal("source", recorder.LastConfig!.Source);
    }

    [Fact]
    public async Task TransferConfigHostedService_ForwardsOptions()
    {
        var recorder = new RecordingConfigurationMigrationService();
        var options = Options.Create(new TransferConfigCommandConfiguration
        {
            Source = "source",
            Delete = true,
            UpdateLocations = false,
            ImportSpeedDevices = true
        });
        var service = new TransferConfigCommandHostedService(recorder, options);

        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(recorder.LastConfig);
        Assert.Equal("source", recorder.LastConfig!.Source);
        Assert.True(recorder.LastConfig.Delete);
        Assert.False(recorder.LastConfig.UpdateLocations);
        Assert.True(recorder.LastConfig.ImportSpeedDevices);
    }

    [Fact]
    public async Task TransferEventHostedService_ForwardsOptions()
    {
        var recorder = new RecordingEventLogMigrationService();
        var options = Options.Create(new MigrationCommandConfiguration
        {
            Source = "source",
            Start = new DateTime(2024, 1, 1, 0, 0, 0),
            End = new DateTime(2024, 1, 1, 1, 0, 0),
            Batch = 1000,
            Device = 7,
            Locations = "1234"
        });
        var service = new TransferEventLogsHostedService(recorder, options);

        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(recorder.LastConfig);
        Assert.Equal("source", recorder.LastConfig!.Source);
        Assert.Equal(1000, recorder.LastConfig.Batch);
        Assert.Equal(7, recorder.LastConfig.Device);
        Assert.Equal("1234", recorder.LastConfig.Locations);
    }

    [Fact]
    public async Task TransferSpeedHostedService_ForwardsOptions()
    {
        var recorder = new RecordingSpeedEventMigrationService();
        var options = Options.Create(new MigrationCommandConfiguration
        {
            Source = "source",
            Start = new DateTime(2024, 1, 1),
            End = new DateTime(2024, 1, 2)
        });
        var service = new TransferSpeedEventsHostedService(recorder, options);

        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(recorder.LastConfig);
        Assert.Equal("source", recorder.LastConfig!.Source);
        Assert.Equal(new DateTime(2024, 1, 1), recorder.LastConfig.Start);
        Assert.Equal(new DateTime(2024, 1, 2), recorder.LastConfig.End);
    }

    [Fact]
    public async Task UpgradeHostedService_RunsAllStepsInOrder()
    {
        var invocations = new List<string>();
        var configRecorder = new RecordingConfigurationMigrationService(invocations);
        var eventRecorder = new RecordingEventLogMigrationService(invocations);
        var speedRecorder = new RecordingSpeedEventMigrationService(invocations);
        var logger = new RecordingLogger<UpgradeTo5HostedService>();
        var options = Options.Create(new UpgradeTo5CommandConfiguration
        {
            Source = "source",
            Start = new DateTime(2024, 2, 1),
            End = new DateTime(2024, 2, 2),
            Delete = true,
            UpdateLocations = false,
            ImportSpeedDevices = true,
            Batch = 500,
            Device = 3,
            Locations = "A1,B2"
        });
        var service = new UpgradeTo5HostedService(
            logger,
            configRecorder,
            eventRecorder,
            speedRecorder,
            options);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["config", "events", "speed"], invocations);

        Assert.NotNull(configRecorder.LastConfig);
        Assert.Equal("source", configRecorder.LastConfig!.Source);
        Assert.True(configRecorder.LastConfig.Delete);
        Assert.False(configRecorder.LastConfig.UpdateLocations);
        Assert.True(configRecorder.LastConfig.ImportSpeedDevices);

        Assert.NotNull(eventRecorder.LastConfig);
        Assert.Equal(500, eventRecorder.LastConfig!.Batch);
        Assert.Equal(3, eventRecorder.LastConfig.Device);
        Assert.Equal("A1,B2", eventRecorder.LastConfig.Locations);

        Assert.NotNull(speedRecorder.LastConfig);
        Assert.Equal(new DateTime(2024, 2, 1), speedRecorder.LastConfig!.Start);
        Assert.Equal(new DateTime(2024, 2, 2), speedRecorder.LastConfig.End);
        Assert.Equal("A1,B2", speedRecorder.LastConfig.Locations);

        Assert.Contains(logger.Messages, message => message.Contains("Completed configuration migration in", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("Completed event log migration in", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("Completed speed event migration in", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("Upgrade-to-5 orchestration completed in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpgradeHostedService_RespectsSkipFlags()
    {
        var invocations = new List<string>();
        var configRecorder = new RecordingConfigurationMigrationService(invocations);
        var eventRecorder = new RecordingEventLogMigrationService(invocations);
        var speedRecorder = new RecordingSpeedEventMigrationService(invocations);
        var options = Options.Create(new UpgradeTo5CommandConfiguration
        {
            Source = "source",
            Start = new DateTime(2024, 3, 1),
            End = new DateTime(2024, 3, 2),
            SkipConfig = true,
            SkipSpeed = true
        });
        var service = new UpgradeTo5HostedService(
            NullLogger<UpgradeTo5HostedService>.Instance,
            configRecorder,
            eventRecorder,
            speedRecorder,
            options);

        await service.StartAsync(CancellationToken.None);

        Assert.Null(configRecorder.LastConfig);
        Assert.NotNull(eventRecorder.LastConfig);
        Assert.Null(speedRecorder.LastConfig);
        Assert.Equal(["events"], invocations);
    }

    [Fact]
    public async Task UpgradeHostedService_RespectsSkipConfigAndSkipEvents()
    {
        var invocations = new List<string>();
        var service = new UpgradeTo5HostedService(
            NullLogger<UpgradeTo5HostedService>.Instance,
            new RecordingConfigurationMigrationService(invocations),
            new RecordingEventLogMigrationService(invocations),
            new RecordingSpeedEventMigrationService(invocations),
            Options.Create(new UpgradeTo5CommandConfiguration
            {
                Source = "source",
                Start = new DateTime(2024, 4, 1),
                End = new DateTime(2024, 4, 2),
                SkipConfig = true,
                SkipEvents = true
            }));

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["speed"], invocations);
    }

    [Fact]
    public async Task UpgradeHostedService_RespectsSkipEventsAndSkipSpeed()
    {
        var invocations = new List<string>();
        var service = new UpgradeTo5HostedService(
            NullLogger<UpgradeTo5HostedService>.Instance,
            new RecordingConfigurationMigrationService(invocations),
            new RecordingEventLogMigrationService(invocations),
            new RecordingSpeedEventMigrationService(invocations),
            Options.Create(new UpgradeTo5CommandConfiguration
            {
                Source = "source",
                Start = new DateTime(2024, 5, 1),
                End = new DateTime(2024, 5, 2),
                SkipEvents = true,
                SkipSpeed = true
            }));

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["config"], invocations);
    }

    [Fact]
    public async Task UpgradeHostedService_ConfigOnlyWithoutSourceFailsBeforeRunningAnyPhase()
    {
        var invocations = new List<string>();
        var service = new UpgradeTo5HostedService(
            NullLogger<UpgradeTo5HostedService>.Instance,
            new RecordingConfigurationMigrationService(invocations),
            new RecordingEventLogMigrationService(invocations),
            new RecordingSpeedEventMigrationService(invocations),
            Options.Create(new UpgradeTo5CommandConfiguration
            {
                SkipEvents = true,
                SkipSpeed = true
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("source connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(invocations);
    }

    [Fact]
    public async Task UpgradeHostedService_AllPhasesSkippedFailsInsteadOfReportingSuccess()
    {
        var invocations = new List<string>();
        var service = new UpgradeTo5HostedService(
            NullLogger<UpgradeTo5HostedService>.Instance,
            new RecordingConfigurationMigrationService(invocations),
            new RecordingEventLogMigrationService(invocations),
            new RecordingSpeedEventMigrationService(invocations),
            Options.Create(new UpgradeTo5CommandConfiguration
            {
                SkipConfig = true,
                SkipEvents = true,
                SkipSpeed = true
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Contains("at least one migration phase", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(invocations);
    }

    [Fact]
    public async Task UpgradeHostedService_StopsAfterConfigurationFailure()
    {
        var invocations = new List<string>();
        var logger = new RecordingLogger<UpgradeTo5HostedService>();
        var service = new UpgradeTo5HostedService(
            logger,
            new ThrowingConfigurationMigrationService(invocations, new InvalidOperationException("config failed")),
            new RecordingEventLogMigrationService(invocations),
            new RecordingSpeedEventMigrationService(invocations),
            Options.Create(new UpgradeTo5CommandConfiguration
            {
                Source = "source",
                Start = new DateTime(2024, 6, 1),
                End = new DateTime(2024, 6, 2)
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

        Assert.Equal("config failed", exception.Message);
        Assert.Equal(["config"], invocations);
        Assert.Contains(logger.Messages, message => message.Contains("configuration migration failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpgradeHostedService_StopsAfterEventFailure()
    {
        var invocations = new List<string>();
        var logger = new RecordingLogger<UpgradeTo5HostedService>();
        var service = new UpgradeTo5HostedService(
            logger,
            new RecordingConfigurationMigrationService(invocations),
            new ThrowingEventLogMigrationService(invocations, new InvalidOperationException("events failed")),
            new RecordingSpeedEventMigrationService(invocations),
            Options.Create(new UpgradeTo5CommandConfiguration
            {
                Source = "source",
                Start = new DateTime(2024, 7, 1),
                End = new DateTime(2024, 7, 2)
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

        Assert.Equal("events failed", exception.Message);
        Assert.Equal(["config", "events"], invocations);
        Assert.Contains(logger.Messages, message => message.Contains("event log migration failed", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingConfigurationMigrationService : IConfigurationMigrationService
    {
        private readonly List<string> _invocations;

        public RecordingConfigurationMigrationService(List<string>? invocations = null)
        {
            _invocations = invocations ?? [];
        }

        public TransferConfigCommandConfiguration? LastConfig { get; private set; }

        public Task RunAsync(TransferConfigCommandConfiguration config, CancellationToken cancellationToken)
        {
            LastConfig = config;
            _invocations.Add("config");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingConfigurationMigrationService : IConfigurationMigrationService
    {
        private readonly List<string> _invocations;
        private readonly Exception _exception;

        public ThrowingConfigurationMigrationService(List<string> invocations, Exception exception)
        {
            _invocations = invocations;
            _exception = exception;
        }

        public Task RunAsync(TransferConfigCommandConfiguration config, CancellationToken cancellationToken)
        {
            _invocations.Add("config");
            return Task.FromException(_exception);
        }
    }

    private sealed class RecordingEventLogMigrationService : IEventLogMigrationService
    {
        private readonly List<string> _invocations;

        public RecordingEventLogMigrationService(List<string>? invocations = null)
        {
            _invocations = invocations ?? [];
        }

        public MigrationCommandConfiguration? LastConfig { get; private set; }

        public Task RunAsync(MigrationCommandConfiguration config, CancellationToken cancellationToken)
        {
            LastConfig = config;
            _invocations.Add("events");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEventLogMigrationService : IEventLogMigrationService
    {
        private readonly List<string> _invocations;
        private readonly Exception _exception;

        public ThrowingEventLogMigrationService(List<string> invocations, Exception exception)
        {
            _invocations = invocations;
            _exception = exception;
        }

        public Task RunAsync(MigrationCommandConfiguration config, CancellationToken cancellationToken)
        {
            _invocations.Add("events");
            return Task.FromException(_exception);
        }
    }

    private sealed class RecordingSpeedEventMigrationService : ISpeedEventMigrationService
    {
        private readonly List<string> _invocations;

        public RecordingSpeedEventMigrationService(List<string>? invocations = null)
        {
            _invocations = invocations ?? [];
        }

        public MigrationCommandConfiguration? LastConfig { get; private set; }

        public Task RunAsync(MigrationCommandConfiguration config, CancellationToken cancellationToken)
        {
            LastConfig = config;
            _invocations.Add("speed");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
