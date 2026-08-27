using System.Collections.Concurrent;
using System.Reflection;
using DataMigrator.Commands;
using DataMigrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Xunit;

namespace DataMigrator.Tests;

public sealed class MigrationWorkflowReliabilityTests
{
    [Fact]
    public async Task SpeedRunAsync_HappyPath_LoadsAndPersistsOneWindow()
    {
        using var provider = BuildEventLogProvider();
        var location = CreateSpeedLocation();
        var repository = CreateLocationRepository(location);
        var loadCalls = 0;

        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            repository.Object,
            (start, end, _, logs, currentLocation, _) =>
            {
                loadCalls++;
                logs.Add(CreateSpeedLog(currentLocation, start, end));
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);

        await service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
        var stored = await context.SpeedEvents.SingleAsync();
        Assert.Equal(1, loadCalls);
        Assert.Equal(location.LocationIdentifier, stored.LocationIdentifier);
        Assert.Equal(location.Devices.Single().Id, stored.DeviceId);
        Assert.Single(stored.Data);
    }

    [Fact]
    public async Task SpeedRunAsync_RetriesTwiceThenCompletes()
    {
        using var provider = BuildEventLogProvider();
        var location = CreateSpeedLocation();
        var repository = CreateLocationRepository(location);
        var attempts = 0;
        var delays = 0;

        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            repository.Object,
            (start, end, _, logs, currentLocation, _) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new InvalidOperationException("transient source failure");
                }

                logs.Add(CreateSpeedLog(currentLocation, start, end));
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        await service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal(2, delays);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task SpeedRunAsync_PropagatesThirdSourceFailureWithoutWriting()
    {
        using var provider = BuildEventLogProvider();
        var repository = CreateLocationRepository(CreateSpeedLocation());
        var attempts = 0;
        var delays = 0;

        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            repository.Object,
            (_, _, _, _, _, _) =>
            {
                attempts++;
                return Task.FromException(new InvalidOperationException("source unavailable"));
            },
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None));

        Assert.Equal("source unavailable", exception.Message);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delays);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task SpeedRunAsync_RerunReplacesExistingWindowWithoutDuplicates()
    {
        using var provider = BuildEventLogProvider();
        var location = CreateSpeedLocation();
        var repository = CreateLocationRepository(location);
        var loadCalls = 0;
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            repository.Object,
            (start, end, _, logs, currentLocation, _) =>
            {
                loadCalls++;
                logs.Add(CreateSpeedLog(currentLocation, start, end, mph: 30 + loadCalls));
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);

        await service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None);
        await service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.SingleAsync();
        Assert.Equal(2, loadCalls);
        Assert.Equal(32, stored.Data.Single().Mph);
    }

    [Fact]
    public async Task SpeedRunAsync_OneTerminalLocationFailurePreventsPartialHourWrite()
    {
        using var provider = BuildEventLogProvider();
        var goodLocation = CreateSpeedLocation("GOOD", 41);
        var badLocation = CreateSpeedLocation("BAD", 42);
        var repository = CreateLocationRepository(goodLocation, badLocation);
        var badAttempts = 0;
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            repository.Object,
            (start, end, _, logs, location, _) =>
            {
                if (location.LocationIdentifier == "BAD")
                {
                    badAttempts++;
                    throw new InvalidOperationException("bad location source failed");
                }

                logs.Add(CreateSpeedLog(location, start, end));
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None));

        Assert.Equal(3, badAttempts);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task SpeedRunAsync_LoadsLocationsOnceAcrossMultipleHours()
    {
        using var provider = BuildEventLogProvider();
        var repository = CreateLocationRepository(CreateSpeedLocation());
        var sourceCalls = 0;
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            repository.Object,
            (_, _, _, _, _, _) =>
            {
                Interlocked.Increment(ref sourceCalls);
                return Task.CompletedTask;
            },
            (_, _) => Task.CompletedTask);
        var configuration = CreateMigrationConfiguration();
        configuration.End = configuration.Start.AddHours(2).AddTicks(-1);

        await service.RunAsync(configuration, CancellationToken.None);

        repository.Verify(value => value.GetList(), Times.Once);
        Assert.Equal(2, sourceCalls);
    }

    [Fact]
    public async Task SpeedRunAsync_PreservesTerminalSourceException()
    {
        using var provider = BuildEventLogProvider();
        var expected = new NotSupportedException("unsupported source shape");
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            CreateLocationRepository(CreateSpeedLocation()).Object,
            (_, _, _, _, _, _) => Task.FromException(expected),
            (_, _) => Task.CompletedTask);

        var actual = await Assert.ThrowsAsync<NotSupportedException>(
            () => service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task EventRunAsync_TerminalSourceFailureIsReportedWithoutWriting()
    {
        using var provider = BuildEventLogProvider();
        var location = CreateEventLocation();
        var repository = CreateLocationRepository(location);
        var service = new EventLogMigrationService(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            repository.Object,
            Policy.Handle<Exception>().RetryAsync(0),
            (_, _, _, _, _) => Task.FromException<CompressedEventLogs<IndianaEvent>?>(
                new InvalidOperationException("event source unavailable")));

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None));

        Assert.Contains(exception.InnerExceptions, failure => failure.Message == "event source unavailable");
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.CountAsync());
    }

    [Fact]
    public async Task EventRunAsync_MixedLocationFailureDoesNotWriteSuccessfulLocation()
    {
        using var provider = BuildEventLogProvider();
        var goodLocation = CreateEventLocation("GOOD", 7);
        var badLocation = CreateEventLocation("BAD", 8);
        var repository = CreateLocationRepository(goodLocation, badLocation);
        var service = new EventLogMigrationService(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            repository.Object,
            Policy.Handle<Exception>().RetryAsync(0),
            (start, end, _, location, _) => location.LocationIdentifier == "BAD"
                ? Task.FromException<CompressedEventLogs<IndianaEvent>?>(new InvalidOperationException("bad event location"))
                : Task.FromResult<CompressedEventLogs<IndianaEvent>?>(CreateEventLog(location, start, end)));

        await Assert.ThrowsAsync<AggregateException>(
            () => service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None));

        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.CountAsync());
    }

    [Fact]
    public async Task EventRunAsync_RedactsPasswordFromLogsAndReportedFailure()
    {
        const string secret = "client-secret-937";
        const string connectionString = "Server=sql01;User Id=migrator;Password=client-secret-937;Database=MOE";
        using var provider = BuildEventLogProvider();
        var logger = new CapturingLogger<EventLogMigrationService>();
        var service = new EventLogMigrationService(
            logger,
            provider,
            CreateLocationRepository(CreateEventLocation()).Object,
            Policy.Handle<Exception>().RetryAsync(0),
            (_, _, _, _, _) => Task.FromException<CompressedEventLogs<IndianaEvent>?>(
                new InvalidOperationException($"Could not connect with {connectionString}")));

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => service.RunAsync(CreateMigrationConfiguration(), CancellationToken.None));
        var loggedText = string.Join(Environment.NewLine, logger.Messages);

        Assert.DoesNotContain(secret, loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, loggedText, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("Password=***", loggedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventRunAsync_SourceCancellationIsPropagatedWithoutWriting()
    {
        using var provider = BuildEventLogProvider();
        var cancellation = new CancellationTokenSource();
        var repository = CreateLocationRepository(CreateEventLocation());
        var service = new EventLogMigrationService(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            repository.Object,
            Policy.Handle<Exception>().RetryAsync(0),
            (_, _, _, _, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<CompressedEventLogs<IndianaEvent>?>(token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(CreateMigrationConfiguration(), cancellation.Token));

        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.CountAsync());
    }

    [Fact]
    public async Task EventRunAsync_RejectsInvalidBatchBeforeReadingLocations()
    {
        using var provider = BuildEventLogProvider();
        var repository = new Mock<ILocationRepository>();
        var service = CreateEventService(provider, Policy.Handle<Exception>().RetryAsync(0), repository.Object);
        var configuration = CreateMigrationConfiguration();
        configuration.Batch = 0;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RunAsync(configuration, CancellationToken.None));

        repository.Verify(value => value.GetList(), Times.Never);
    }

    [Fact]
    public async Task EventRunAsync_RejectsBatchAboveSqlServerParameterLimitBeforeReadingLocations()
    {
        using var provider = BuildEventLogProvider();
        var repository = new Mock<ILocationRepository>();
        var service = CreateEventService(provider, Policy.Handle<Exception>().RetryAsync(0), repository.Object);
        var configuration = CreateMigrationConfiguration();
        configuration.Batch = EventLogMigrationService.MaxInsertBatchSize + 1;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RunAsync(configuration, CancellationToken.None));

        repository.Verify(value => value.GetList(), Times.Never);
    }

    [Fact]
    public async Task EventRunAsync_LoadsLocationsOnceAndLimitsSourceConcurrency()
    {
        using var provider = BuildEventLogProvider();
        var locations = Enumerable.Range(1, 25)
            .Select(index => CreateEventLocation($"L{index}", index))
            .ToArray();
        var repository = CreateLocationRepository(locations);
        var currentConcurrency = 0;
        var maximumConcurrency = 0;
        var sourceCalls = 0;
        var service = new EventLogMigrationService(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            repository.Object,
            Policy.Handle<Exception>().RetryAsync(0),
            async (_, _, _, _, token) =>
            {
                Interlocked.Increment(ref sourceCalls);
                var current = Interlocked.Increment(ref currentConcurrency);
                int observed;
                do
                {
                    observed = maximumConcurrency;
                }
                while (current > observed && Interlocked.CompareExchange(ref maximumConcurrency, current, observed) != observed);

                await Task.Delay(20, token);
                Interlocked.Decrement(ref currentConcurrency);
                return null;
            });
        var configuration = CreateMigrationConfiguration();
        configuration.End = configuration.Start.AddHours(2).AddTicks(-1);

        await service.RunAsync(configuration, CancellationToken.None);

        repository.Verify(value => value.GetList(), Times.Once);
        Assert.Equal(50, sourceCalls);
        Assert.InRange(maximumConcurrency, 1, 10);
    }

    [Fact]
    public async Task EventRunAsync_HonorsInsertBatchAcrossBoundedSourceReadGroups()
    {
        var interceptor = new FailingSaveChangesInterceptor(failuresBeforeSuccess: 0);
        using var provider = BuildEventLogProvider(interceptor);
        var locations = Enumerable.Range(0, 25)
            .Select(index => CreateEventLocation($"L{index:D3}", index + 1))
            .ToArray();
        var firstBatchPersistedBeforeThirdGroup = false;
        var service = new EventLogMigrationService(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            CreateLocationRepository(locations).Object,
            Policy.Handle<Exception>().RetryAsync(0),
            (start, end, _, location, _) =>
            {
                if (location.LocationIdentifier == "L020")
                {
                    using var scope = provider.CreateScope();
                    firstBatchPersistedBeforeThirdGroup = scope.ServiceProvider
                        .GetRequiredService<EventLogContext>()
                        .IndiannaEvents
                        .Any();
                }

                return Task.FromResult<CompressedEventLogs<IndianaEvent>?>(CreateEventLog(location, start, end));
            });

        var configuration = CreateMigrationConfiguration();
        configuration.Batch = 20;
        await service.RunAsync(configuration, CancellationToken.None);

        Assert.True(firstBatchPersistedBeforeThirdGroup);
        Assert.Equal(2, interceptor.Attempts);
        await using var verificationScope = provider.CreateAsyncScope();
        Assert.Equal(25, await verificationScope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.CountAsync());
    }

    [Fact]
    public async Task EventRunAsync_DateOnlyEndIncludesWholeDay()
    {
        using var provider = BuildEventLogProvider();
        var sourceCalls = 0;
        var location = CreateEventLocation();
        var service = new EventLogMigrationService(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            CreateLocationRepository(location).Object,
            Policy.Handle<Exception>().RetryAsync(0),
            (_, _, _, _, _) =>
            {
                Interlocked.Increment(ref sourceCalls);
                return Task.FromResult<CompressedEventLogs<IndianaEvent>?>(null);
            });
        var configuration = CreateMigrationConfiguration();
        configuration.Start = new DateTime(2026, 1, 1);
        configuration.End = new DateTime(2026, 1, 1);
        configuration.EndIsDateOnly = true;

        await service.RunAsync(configuration, CancellationToken.None);

        Assert.Equal(24, sourceCalls);
    }

    [Fact]
    public async Task MigrationServices_RejectEndBeforeStartBeforeReadingLocations()
    {
        using var provider = BuildEventLogProvider();
        var eventRepository = new Mock<ILocationRepository>();
        var speedRepository = new Mock<ILocationRepository>();
        var configuration = CreateMigrationConfiguration();
        configuration.End = configuration.Start.AddTicks(-1);
        var eventService = CreateEventService(provider, Policy.Handle<Exception>().RetryAsync(0), eventRepository.Object);
        var speedService = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            speedRepository.Object);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => eventService.RunAsync(configuration, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => speedService.RunAsync(configuration, CancellationToken.None));

        eventRepository.Verify(value => value.GetList(), Times.Never);
        speedRepository.Verify(value => value.GetList(), Times.Never);
    }

    [Fact]
    public async Task EventInsert_RetriesTransientTargetFailureAndPersistsBatch()
    {
        var interceptor = new FailingSaveChangesInterceptor(failuresBeforeSuccess: 1);
        using var provider = BuildEventLogProvider(interceptor);
        var policy = Policy.Handle<Exception>().RetryAsync(2);
        var service = CreateEventService(provider, policy);
        var log = CreateEventLog();

        await InvokeEventInsertAsync(service, [log]);

        Assert.Equal(2, interceptor.Attempts);
        await using var scope = provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.SingleAsync();
        Assert.Equal(log.LocationIdentifier, stored.LocationIdentifier);
        Assert.Single(stored.Data);
    }

    [Fact]
    public async Task EventInsert_PropagatesFailureAfterRetryPolicyIsExhausted()
    {
        var interceptor = new FailingSaveChangesInterceptor(failuresBeforeSuccess: int.MaxValue);
        using var provider = BuildEventLogProvider(interceptor);
        var service = CreateEventService(provider, Policy.Handle<Exception>().RetryAsync(2));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEventInsertAsync(service, [CreateEventLog()]));

        Assert.Equal("simulated target failure", exception.Message);
        Assert.Equal(3, interceptor.Attempts);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.CountAsync());
    }

    [Fact]
    public async Task EventInsert_RerunReplacesExistingWindowWithoutDuplicates()
    {
        using var provider = BuildEventLogProvider();
        var service = CreateEventService(provider, Policy.Handle<Exception>().RetryAsync(0));

        await InvokeEventInsertAsync(service, [CreateEventLog(eventCode: 1)]);
        await InvokeEventInsertAsync(service, [CreateEventLog(eventCode: 9)]);

        await using var scope = provider.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.SingleAsync();
        Assert.Equal(9, stored.Data.Single().EventCode);
    }

    [Fact]
    public async Task EventInsert_FailedReplacementRollsBackOriginalWindow()
    {
        var interceptor = new ScriptedSaveChangesInterceptor(2);
        using var provider = BuildEventLogProvider(interceptor);
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<EventLogContext>();
            seedContext.IndiannaEvents.Add(CreateEventLog(eventCode: 1));
            await seedContext.SaveChangesAsync();
        }

        var service = CreateEventService(provider, Policy.Handle<Exception>().RetryAsync(0));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEventInsertAsync(service, [CreateEventLog(eventCode: 9)]));

        await using var verificationScope = provider.CreateAsyncScope();
        var stored = await verificationScope.ServiceProvider.GetRequiredService<EventLogContext>().IndiannaEvents.SingleAsync();
        Assert.Equal(1, stored.Data.Single().EventCode);
    }

    [Fact]
    public async Task SpeedFlush_BulkFailureFallsBackToIndividualInsert()
    {
        var interceptor = new FailingSaveChangesInterceptor(failuresBeforeSuccess: 1);
        using var provider = BuildEventLogProvider(interceptor);
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            new Mock<ILocationRepository>().Object);
        var location = CreateSpeedLocation();
        var logs = new ConcurrentBag<CompressedEventLogs<SpeedEvent>>(
            [CreateSpeedLog(location, new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 10, 0, 0))]);

        await InvokeSpeedFlushAsync(service, logs);

        Assert.Equal(2, interceptor.Attempts);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task SpeedFlush_OneFailedFallbackItemAttemptsNextItemAndReportsFailure()
    {
        var interceptor = new ScriptedSaveChangesInterceptor(1, 2);
        using var provider = BuildEventLogProvider(interceptor);
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            new Mock<ILocationRepository>().Object);
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var logs = new ConcurrentBag<CompressedEventLogs<SpeedEvent>>(
        [
            CreateSpeedLog(CreateSpeedLocation("S100", 41), start, start.AddHours(1)),
            CreateSpeedLog(CreateSpeedLocation("S200", 42), start, start.AddHours(1))
        ]);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => InvokeSpeedFlushAsync(service, logs));

        Assert.Equal(3, interceptor.Attempts);
        Assert.Single(exception.InnerExceptions);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task SpeedFlush_AllFallbackWritesFailAndReportFailure()
    {
        var interceptor = new FailingSaveChangesInterceptor(failuresBeforeSuccess: int.MaxValue);
        using var provider = BuildEventLogProvider(interceptor);
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            new Mock<ILocationRepository>().Object);
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var logs = new ConcurrentBag<CompressedEventLogs<SpeedEvent>>(
            [CreateSpeedLog(CreateSpeedLocation(), start, start.AddHours(1))]);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => InvokeSpeedFlushAsync(service, logs));

        Assert.Single(exception.InnerExceptions);
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task SpeedFlush_CancellationIsPropagatedWithoutWriting()
    {
        using var provider = BuildEventLogProvider();
        var service = new SpeedEventMigrationService(
            NullLogger<SpeedEventMigrationService>.Instance,
            provider,
            new Mock<ILocationRepository>().Object);
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var logs = new ConcurrentBag<CompressedEventLogs<SpeedEvent>>(
            [CreateSpeedLog(CreateSpeedLocation(), start, start.AddHours(1))]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeSpeedFlushAsync(service, logs, cancellation.Token));

        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<EventLogContext>().SpeedEvents.CountAsync());
    }

    [Fact]
    public async Task ConfigurationRun_DeleteWithoutSourceFailsBeforeRemovingAnything()
    {
        var locationRepository = new Mock<ILocationRepository>();
        var service = CreateConfigurationService(locationRepository.Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(
                new TransferConfigCommandConfiguration { Delete = true, Source = "" },
                CancellationToken.None));

        Assert.Contains("source connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
        locationRepository.Verify(
            repository => repository.RemoveRange(It.IsAny<IEnumerable<Location>>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfigurationRun_PreCanceledRequestFailsBeforeRemovingAnything()
    {
        var locationRepository = new Mock<ILocationRepository>();
        var service = CreateConfigurationService(locationRepository.Object);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(
                new TransferConfigCommandConfiguration { Delete = true, Source = "configured source" },
                cancellation.Token));

        locationRepository.Verify(
            repository => repository.RemoveRange(It.IsAny<IEnumerable<Location>>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfigurationRun_FailedSourcePreflightDoesNotRemoveAnything()
    {
        var locationRepository = new Mock<ILocationRepository>();
        var service = CreateConfigurationService(locationRepository.Object);
        var preflightCalls = 0;
        service.SourcePreflightAsync = (_, _) =>
        {
            preflightCalls++;
            return Task.FromException(new InvalidOperationException("source schema is incompatible"));
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(
                new TransferConfigCommandConfiguration { Delete = true, Source = "nonempty-invalid-source" },
                CancellationToken.None));

        Assert.Equal("source schema is incompatible", exception.Message);
        Assert.Equal(1, preflightCalls);
        locationRepository.Verify(
            repository => repository.RemoveRange(It.IsAny<IEnumerable<Location>>()),
            Times.Never);
    }

    private static ServiceProvider BuildEventLogProvider(SaveChangesInterceptor? interceptor = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddSingleton(connection);
        services.AddDbContext<EventLogContext>((serviceProvider, options) =>
        {
            options.UseSqlite(serviceProvider.GetRequiredService<SqliteConnection>());
            if (interceptor != null)
            {
                options.AddInterceptors(interceptor);
            }
        });

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<EventLogContext>().Database.EnsureCreated();
        return provider;
    }

    private static Mock<ILocationRepository> CreateLocationRepository(params Location[] locations)
    {
        var repository = new Mock<ILocationRepository>();
        repository.Setup(value => value.GetList()).Returns(locations.AsQueryable());
        return repository;
    }

    private static Location CreateSpeedLocation(string identifier = "S100", int deviceId = 42) => new()
    {
        LocationIdentifier = identifier,
        Start = new DateTime(2025, 1, 1),
        Devices = [new Device { Id = deviceId, DeviceType = DeviceTypes.SpeedSensor }],
        Approaches = []
    };

    private static Location CreateEventLocation(string identifier = "L100", int deviceId = 7) => new()
    {
        LocationIdentifier = identifier,
        Start = new DateTime(2025, 1, 1),
        Devices = [new Device { Id = deviceId, DeviceType = DeviceTypes.SignalController }]
    };

    private static MigrationCommandConfiguration CreateMigrationConfiguration() => new()
    {
        Source = "unused by injected source loader",
        Start = new DateTime(2026, 1, 1, 9, 0, 0),
        End = new DateTime(2026, 1, 1, 9, 30, 0)
    };

    private static CompressedEventLogs<SpeedEvent> CreateSpeedLog(Location location, DateTime start, DateTime end, int mph = 35) => new()
    {
        LocationIdentifier = location.LocationIdentifier,
        DeviceId = location.Devices.Single().Id,
        Start = start,
        End = end,
        Data =
        [
            new SpeedEvent
            {
                DetectorId = "S10001",
                Mph = mph,
                Kph = 56,
                Timestamp = start.AddMinutes(1)
            }
        ]
    };

    private static CompressedEventLogs<IndianaEvent> CreateEventLog(short eventCode = 1) => new()
    {
        LocationIdentifier = "L100",
        DeviceId = 7,
        Start = new DateTime(2026, 1, 1, 9, 0, 0),
        End = new DateTime(2026, 1, 1, 10, 0, 0),
        Data = [new IndianaEvent { Timestamp = new DateTime(2026, 1, 1, 9, 1, 0), EventCode = eventCode, EventParam = 2 }]
    };

    private static CompressedEventLogs<IndianaEvent> CreateEventLog(Location location, DateTime start, DateTime end) => new()
    {
        LocationIdentifier = location.LocationIdentifier,
        DeviceId = location.Devices.Single().Id,
        Start = start,
        End = end,
        Data = [new IndianaEvent { Timestamp = start.AddMinutes(1), EventCode = 1, EventParam = 2 }]
    };

    private static EventLogMigrationService CreateEventService(
        ServiceProvider provider,
        Polly.Retry.AsyncRetryPolicy policy,
        ILocationRepository? locationRepository = null) =>
        new(
            NullLogger<EventLogMigrationService>.Instance,
            provider,
            locationRepository ?? new Mock<ILocationRepository>().Object,
            policy);

    private static ConfigurationMigrationService CreateConfigurationService(ILocationRepository locationRepository) =>
        new(
            NullLogger<ConfigurationMigrationService>.Instance,
            new Mock<IJurisdictionRepository>().Object,
            new Mock<ILocationTypeRepository>().Object,
            locationRepository,
            new Mock<IApproachRepository>().Object,
            new Mock<IDetectorRepository>().Object,
            new Mock<IDeviceRepository>().Object,
            new Mock<IDeviceConfigurationRepository>().Object,
            new Mock<IProductRepository>().Object,
            new Mock<IRegionsRepository>().Object,
            new Mock<IAreaRepository>().Object,
            new Mock<IDetectionTypeRepository>().Object,
            new Mock<IMeasureTypeRepository>().Object,
            new Mock<IRouteRepository>().Object,
            new Mock<IRouteLocationsRepository>().Object,
            new Mock<IServiceProvider>().Object);

    private static async Task InvokeEventInsertAsync(
        EventLogMigrationService service,
        List<CompressedEventLogs<IndianaEvent>> logs)
    {
        var method = typeof(EventLogMigrationService).GetMethod("InsertLogsWithRetryAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task)method.Invoke(
            service,
            [logs, new MigrationCommandConfiguration { Batch = 500 }, CancellationToken.None])!;
        await task;
    }

    private static async Task InvokeSpeedFlushAsync(
        SpeedEventMigrationService service,
        ConcurrentBag<CompressedEventLogs<SpeedEvent>> logs,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(SpeedEventMigrationService).GetMethod("FlushLogsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        await (Task)method.Invoke(service, [logs, cancellationToken])!;
    }

    private sealed class FailingSaveChangesInterceptor(int failuresBeforeSuccess) : SaveChangesInterceptor
    {
        private int _remainingFailures = failuresBeforeSuccess;

        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (_remainingFailures > 0)
            {
                _remainingFailures--;
                throw new InvalidOperationException("simulated target failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ScriptedSaveChangesInterceptor(params int[] failingAttempts) : SaveChangesInterceptor
    {
        private readonly HashSet<int> _failingAttempts = failingAttempts.ToHashSet();

        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (_failingAttempts.Contains(Attempts))
            {
                throw new InvalidOperationException("simulated target failure");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception != null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }
}
