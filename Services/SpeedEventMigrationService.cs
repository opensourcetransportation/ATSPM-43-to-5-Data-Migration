#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/SpeedEventMigrationService.cs
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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Collections.Concurrent;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Utah.Udot.Atspm.Extensions;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;

namespace DataMigrator.Services;

internal delegate Task SpeedLogLoader(
    DateTime startUtc,
    DateTime endUtc,
    string sourceConnectionString,
    ConcurrentBag<CompressedEventLogs<SpeedEvent>> archiveLogs,
    Location location,
    CancellationToken cancellationToken);

public sealed class SpeedEventMigrationService : ISpeedEventMigrationService
{
    private readonly record struct LogKey(string LocationIdentifier, int DeviceId, DateTime Start);
    private const int SourceQueryTimeoutSeconds = 300;
    internal const int MaxDetectorParametersPerQuery = 2000;

    private readonly ILogger<SpeedEventMigrationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocationRepository _locationRepository;
    private readonly SpeedLogLoader _loadLogsAsync;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public SpeedEventMigrationService(ILogger<SpeedEventMigrationService> logger, IServiceProvider serviceProvider, ILocationRepository locationRepository)
        : this(logger, serviceProvider, locationRepository, null, null)
    {
    }

    internal SpeedEventMigrationService(
        ILogger<SpeedEventMigrationService> logger,
        IServiceProvider serviceProvider,
        ILocationRepository locationRepository,
        SpeedLogLoader? loadLogsAsync,
        Func<TimeSpan, CancellationToken, Task>? delayAsync)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _locationRepository = locationRepository;
        _loadLogsAsync = loadLogsAsync ?? GetLogsAsync;
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public async Task RunAsync(MigrationCommandConfiguration config, CancellationToken cancellationToken)
    {
        ValidateConfiguration(config);
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var endExclusive = MigrationDateRange.NormalizeInclusiveEndToExclusive(config.Start, config.End, config.EndIsDateOnly);
        var processedHours = 0;
        var locations = LoadCurrentLocations(config.Locations);
        var totalLocationReads = locations.Count;
        var totalCompressedWindows = 0;
        var totalSourceEvents = 0;

        _logger.LogInformation("Loaded {Count} speed-migration locations.", locations.Count);

        foreach (var (periodStart, periodEnd) in MigrationDateRange.EnumerateHourlyWindows(config.Start, endExclusive))
        {
            processedHours++;
            _logger.LogInformation("Processing speed events from {Start} to {End}", periodStart, periodEnd);

            var archiveLogs = new ConcurrentBag<CompressedEventLogs<SpeedEvent>>();
            var locationBatches = locations.Select((location, index) => new { location, index }).GroupBy(x => x.index / 10).Select(g => g.Select(x => x.location).ToList());
            foreach (var batch in locationBatches)
            {
                var tasks = batch.Select(location => ProcessLocationWithRetryAsync(periodStart, periodEnd, location, archiveLogs, config.Source, cancellationToken));
                await Task.WhenAll(tasks);
                if (archiveLogs.Count > 50)
                {
                    totalCompressedWindows += archiveLogs.Count;
                    totalSourceEvents += archiveLogs.Sum(log => log.Data.Count);
                    await FlushLogsAsync(archiveLogs, cancellationToken);
                }
            }

            if (!archiveLogs.IsEmpty)
            {
                totalCompressedWindows += archiveLogs.Count;
                totalSourceEvents += archiveLogs.Sum(log => log.Data.Count);
                await FlushLogsAsync(archiveLogs, cancellationToken);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Speed-event migration completed in {ElapsedMs} ms. HoursProcessed={HoursProcessed}, LocationReads={LocationReads}, SourceEventsLoaded={SourceEventsLoaded}, CompressedWindowsWritten={CompressedWindowsWritten}.",
            stopwatch.ElapsedMilliseconds,
            processedHours,
            totalLocationReads,
            totalSourceEvents,
            totalCompressedWindows);
    }

    private async Task ProcessLocationWithRetryAsync(DateTime startUtc, DateTime endUtc, Location location, ConcurrentBag<CompressedEventLogs<SpeedEvent>> archiveLogs, string sourceConnectionString, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await _loadLogsAsync(startUtc, endUtc, sourceConnectionString, archiveLogs, location, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning(
                    "Attempt {Attempt} failed for {Location}. Retrying. {Error}",
                    attempt,
                    location.LocationIdentifier,
                    SensitiveDataRedactor.Redact(ex.Message));
                await _delayAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    private async Task FlushLogsAsync(ConcurrentBag<CompressedEventLogs<SpeedEvent>> archiveLogs, CancellationToken cancellationToken)
    {
        var toInsert = new List<CompressedEventLogs<SpeedEvent>>();
        while (archiveLogs.TryTake(out var item))
        {
            toInsert.Add(item);
        }

        if (!toInsert.Any())
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var existingLogs = await GetExistingLogsAsync(context, toInsert, cancellationToken);
            if (existingLogs.Count != 0)
            {
                _logger.LogInformation(
                    "Replacing {Count} existing speed-event windows before insert for {Start} through {End}.",
                    existingLogs.Count,
                    toInsert.Min(log => log.Start),
                    toInsert.Max(log => log.End));
                context.SpeedEvents.RemoveRange(existingLogs);
                await context.SaveChangesAsync(cancellationToken);
            }

            context.SpeedEvents.AddRange(toInsert);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Flushed {Count} compressed speed-event records.", toInsert.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Bulk speed-event flush failed. Falling back to individual inserts. {Error}",
                SensitiveDataRedactor.Redact(ex.Message));
            var fallbackFailures = new List<Exception>();
            foreach (var log in toInsert)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
                    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                    var existingLogs = await GetExistingLogsAsync(context, [log], cancellationToken);
                    if (existingLogs.Count != 0)
                    {
                        _logger.LogInformation(
                            "Replacing existing speed-event window for {Location} between {Start} and {End} during fallback insert.",
                            log.LocationIdentifier,
                            log.Start,
                            log.End);
                        context.SpeedEvents.RemoveRange(existingLogs);
                        await context.SaveChangesAsync(cancellationToken);
                    }

                    context.SpeedEvents.Add(log);
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception itemEx)
                {
                    _logger.LogError(
                        "Failed to insert speed log for {Location} between {Start} and {End}. {Error}",
                        log.LocationIdentifier,
                        log.Start,
                        log.End,
                        SensitiveDataRedactor.Redact(itemEx.Message));
                    fallbackFailures.Add(new InvalidOperationException(SensitiveDataRedactor.Redact(itemEx.Message)));
                }
            }

            if (fallbackFailures.Count != 0)
            {
                throw new AggregateException(
                    $"Failed to insert {fallbackFailures.Count} of {toInsert.Count} speed-event window(s) during fallback.",
                    fallbackFailures);
            }
        }
    }

    private static void ValidateConfiguration(MigrationCommandConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Source))
        {
            throw new ArgumentException("A source connection string is required.", nameof(config));
        }

        if (config.End < config.Start)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "The migration end must not be before the start.");
        }
    }

    private async Task GetLogsAsync(DateTime startUtc, DateTime endUtc, string sourceConnectionString, ConcurrentBag<CompressedEventLogs<SpeedEvent>> archiveLogs, Location location, CancellationToken cancellationToken)
    {
        var detectorIdentifiers = GetSourceDetectorIdentifiers(location);
        if (detectorIdentifiers.Count == 0)
        {
            _logger.LogWarning("No detector identifiers found for {Location}; skipping source speed query.", location.LocationIdentifier);
            return;
        }

        var eventLogs = new List<SpeedEvent>();
        _logger.LogInformation(
            "Querying source speed events for {Location} between {Start} and {End} using {DetectorCount} exact detector ids.",
            location.LocationIdentifier,
            startUtc,
            endUtc,
            detectorIdentifiers.Count);
        var normalizedSourceConnectionString = BuildSourceConnectionString(sourceConnectionString);
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(normalizedSourceConnectionString);
        await conn.OpenAsync(cancellationToken);
        foreach (var detectorChunk in ChunkDetectorIdentifiers(detectorIdentifiers))
        {
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand(BuildSpeedEventQuery(detectorChunk.Length), conn);
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.Add("@startUtc", SqlDbType.DateTime2).Value = startUtc;
            cmd.Parameters.Add("@endUtc", SqlDbType.DateTime2).Value = endUtc;
            for (var index = 0; index < detectorChunk.Length; index++)
            {
                cmd.Parameters.Add($"@detectorId{index}", SqlDbType.VarChar, 50).Value = detectorChunk[index];
            }

            cmd.CommandTimeout = SourceQueryTimeoutSeconds;
            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                eventLogs.Add(new SpeedEvent
                {
                    DetectorId = reader.GetString(reader.GetOrdinal("DetectorID")),
                    Mph = reader.GetInt32(reader.GetOrdinal("MPH")),
                    Kph = reader.GetInt32(reader.GetOrdinal("KPH")),
                    Timestamp = reader.GetDateTime(reader.GetOrdinal("Timestamp"))
                });
            }
        }

        _logger.LogInformation("Loaded {Count} source speed events for {Location} between {Start} and {End}.", eventLogs.Count, location.LocationIdentifier, startUtc, endUtc);

        if (!eventLogs.Any())
        {
            return;
        }

        var device = location.Devices.FirstOrDefault(d => d.DeviceType == DeviceTypes.SpeedSensor);
        if (device == null)
        {
            _logger.LogWarning("No speed device found for {Location}", location.LocationIdentifier);
            return;
        }

        archiveLogs.Add(new CompressedEventLogs<SpeedEvent>
        {
            LocationIdentifier = location.LocationIdentifier,
            DeviceId = device.Id,
            Start = startUtc,
            End = endUtc,
            Data = eventLogs
        });
    }

    private static async Task<List<CompressedEventLogs<SpeedEvent>>> GetExistingLogsAsync(
        EventLogContext context,
        IReadOnlyCollection<CompressedEventLogs<SpeedEvent>> logs,
        CancellationToken cancellationToken)
    {
        var logKeys = logs.Select(CreateKey).ToHashSet();
        var locationIdentifiers = logKeys.Select(key => key.LocationIdentifier).Distinct().ToList();
        var deviceIds = logKeys.Select(key => key.DeviceId).Distinct().ToList();
        var starts = logKeys.Select(key => key.Start).Distinct().ToList();

        var existingLogs = await context.SpeedEvents
            .Where(log =>
                locationIdentifiers.Contains(log.LocationIdentifier) &&
                deviceIds.Contains(log.DeviceId) &&
                starts.Contains(log.Start))
            .ToListAsync(cancellationToken);

        return existingLogs.Where(log => logKeys.Contains(CreateKey(log))).ToList();
    }

    private static LogKey CreateKey(CompressedEventLogs<SpeedEvent> log) =>
        new(log.LocationIdentifier, log.DeviceId, log.Start);

    private List<Location> LoadCurrentLocations(string? locationIdentifiers)
    {
        var locationsQuery = _locationRepository.GetList()
            .Include(location => location.Devices)
            .Include(location => location.Approaches)
            .ThenInclude(approach => approach.Detectors)
            .AsSplitQuery()
            .Where(location => location.Devices.Any(device => device.DeviceType == DeviceTypes.SpeedSensor))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(locationIdentifiers))
        {
            var allowedIdentifiers = locationIdentifiers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            locationsQuery = locationsQuery
                .Where(location => allowedIdentifiers.Contains(location.LocationIdentifier));
        }

        return locationsQuery
            .Where(location => location.VersionAction != LocationVersionActions.Delete)
            .ToList()
            .GroupBy(location => location.LocationIdentifier)
            .Select(group => group.OrderByDescending(location => location.Start).FirstOrDefault()!)
            .ToList();
    }

    private static List<string> GetSourceDetectorIdentifiers(Location location)
    {
        return location.Approaches
            .SelectMany(approach => approach.Detectors)
            .Select(detector => detector.DectectorIdentifier)
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildSourceConnectionString(string sourceConnectionString)
    {
        var normalizedConnectionString = sourceConnectionString.Trim();
        if (normalizedConnectionString.Length >= 2 &&
            normalizedConnectionString[0] == '"' &&
            normalizedConnectionString[^1] == '"')
        {
            normalizedConnectionString = normalizedConnectionString[1..^1];
        }

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(normalizedConnectionString)
        {
            ApplicationName = "DataMigrator.SpeedMigration",
            MultipleActiveResultSets = false,
            Pooling = false
        };

        if (builder.ConnectTimeout < 60)
        {
            builder.ConnectTimeout = 60;
        }

        return builder.ConnectionString;
    }
    private static string BuildSpeedEventQuery(int detectorCount)
    {
        if (detectorCount is <= 0 or > MaxDetectorParametersPerQuery)
        {
            throw new ArgumentOutOfRangeException(nameof(detectorCount));
        }

        var detectorParameters = string.Join(", ", Enumerable.Range(0, detectorCount).Select(index => $"@detectorId{index}"));

        return $@"
                                SELECT DISTINCT DetectorID, MPH, KPH, Timestamp
                                    FROM [dbo].[Speed_Events]
                                 WHERE Timestamp >= @startUtc
                                     AND Timestamp <  @endUtc
                                     AND DetectorID IN ({detectorParameters})
                                OPTION (RECOMPILE)";
    }

    internal static IEnumerable<string[]> ChunkDetectorIdentifiers(IEnumerable<string> detectorIdentifiers) =>
        detectorIdentifiers.Chunk(MaxDetectorParametersPerQuery);
}






