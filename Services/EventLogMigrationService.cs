#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/EventLogMigrationService.cs
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
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Retry;
using System.Collections.Concurrent;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Utah.Udot.Atspm.Extensions;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Utah.Udot.Atspm.Specifications;
using Utah.Udot.NetStandardToolkit.Extensions;

namespace DataMigrator.Services;

public sealed class EventLogMigrationService : IEventLogMigrationService
{
    private readonly record struct LogKey(string LocationIdentifier, int DeviceId, DateTime Start);

    private readonly ILogger<EventLogMigrationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocationRepository _locationRepository;
    private readonly AsyncRetryPolicy _retryPolicy;

    public EventLogMigrationService(ILogger<EventLogMigrationService> logger, IServiceProvider serviceProvider, ILocationRepository locationRepository, AsyncRetryPolicy? retryPolicy = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _locationRepository = locationRepository;
        if (retryPolicy != null)
        {
            _retryPolicy = retryPolicy;
        }
        else
        {
            // If the SHORT_RETRY_POLICY env var is set, use a very short retry policy useful for tests
            var shortPolicyFlag = Environment.GetEnvironmentVariable("SHORT_RETRY_POLICY");
            if (!string.IsNullOrEmpty(shortPolicyFlag) && shortPolicyFlag == "1")
            {
                _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(new[] { TimeSpan.Zero });
            }
            else
            {
                _retryPolicy = Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(
                        Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(10), 5)
                            .Concat(new[] { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) })
                            .Concat(Enumerable.Repeat(TimeSpan.FromDays(1), 24)),
                        (exception, timeSpan, retryCount, _) => Console.WriteLine($"Retry {retryCount} after {timeSpan.TotalSeconds} seconds due to {exception.Message}"));
            }
        }
    }

    public async Task RunAsync(MigrationCommandConfiguration config, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var endExclusive = MigrationDateRange.NormalizeInclusiveEndToExclusive(config.Start, config.End, treatDateOnlyEndAsWholeDay: false);
        DeviceTypes? requiredDeviceType = config.Device.HasValue
            ? (DeviceTypes)config.Device.Value
            : null;
        var processedHours = 0;
        var totalLocationReads = 0;
        var totalCompressedWindows = 0;
        var totalSourceEvents = 0;

        foreach (var (periodStart, periodEnd) in MigrationDateRange.EnumerateHourlyWindows(config.Start, endExclusive))
        {
            processedHours++;
            _logger.LogInformation("Processing event data from {Start} to {End}", periodStart, periodEnd);
            var locations = LoadCurrentLocations(requiredDeviceType, config.Locations);
            totalLocationReads += locations.Count;

            var hourLogs = new ConcurrentBag<CompressedEventLogs<IndianaEvent>>();
            await Task.WhenAll(locations.Select(async location =>
            {
                try
                {
                    var log = await GetLogsAsync(periodStart, periodEnd, config.Source, location, cancellationToken);
                    if (log != null)
                    {
                        hourLogs.Add(log);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve logs for {Location} between {Start} and {End}", location.LocationIdentifier, periodStart, periodEnd);
                }
            }));

            var hourLogList = hourLogs.ToList();
            totalCompressedWindows += hourLogList.Count;
            totalSourceEvents += hourLogList.Sum(log => log.Data.Count);
            await InsertLogsWithRetryAsync(hourLogList, config, cancellationToken);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Event migration completed in {ElapsedMs} ms. HoursProcessed={HoursProcessed}, LocationReads={LocationReads}, SourceEventsLoaded={SourceEventsLoaded}, CompressedWindowsWritten={CompressedWindowsWritten}.",
            stopwatch.ElapsedMilliseconds,
            processedHours,
            totalLocationReads,
            totalSourceEvents,
            totalCompressedWindows);
    }

    private async Task<CompressedEventLogs<IndianaEvent>?> GetLogsAsync(DateTime start, DateTime end, string sourceConnectionString, Location location, CancellationToken cancellationToken)
    {
        var connectionString = $"{sourceConnectionString};Max Pool Size=200;Connection Timeout=60;";
                const string selectQuery = @"
                        SELECT SignalId, Timestamp, EventCode, EventParam
                        FROM [dbo].[Controller_Event_Log]
                        WHERE SignalId = @locationIdentifier
                            AND Timestamp >= @start
                            AND Timestamp < @end";

        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                using var selectCommand = new SqlCommand(selectQuery, connection);
                selectCommand.Parameters.AddWithValue("@locationIdentifier", location.LocationIdentifier);
                selectCommand.Parameters.AddWithValue("@start", start);
                selectCommand.Parameters.AddWithValue("@end", end);
                selectCommand.CommandTimeout = 120;
                var eventLogs = new List<IndianaEvent>();
                using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    try
                    {
                        eventLogs.Add(new IndianaEvent
                        {
                            Timestamp = (DateTime)reader["Timestamp"],
                            EventCode = Convert.ToInt16(reader["EventCode"]),
                            EventParam = Convert.ToInt16(reader["EventParam"])
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error reading event record for {Location} on {Start}", location.LocationIdentifier, start);
                    }
                }

                if (!eventLogs.Any())
                {
                    return null;
                }

                var device = location.Devices.FirstOrDefault(d => d.DeviceType == DeviceTypes.SignalController);
                if (device == null)
                {
                    _logger.LogWarning("No signal-controller device found for location {Location}", location.LocationIdentifier);
                    return null;
                }

                return new CompressedEventLogs<IndianaEvent>
                {
                    LocationIdentifier = location.LocationIdentifier,
                    DeviceId = device.Id,
                    Start = start,
                    End = end,
                    Data = eventLogs
                };
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get event logs for {Location} on {Start}", location.LocationIdentifier, start);
            return null;
        }
    }

    private async Task InsertLogsWithRetryAsync(List<CompressedEventLogs<IndianaEvent>> archiveLogs, MigrationCommandConfiguration config, CancellationToken cancellationToken)
    {
        if (!archiveLogs.Any())
        {
            return;
        }

        var batchNumber = 1;
        var batchSize = config.Batch ?? 500;
        foreach (var logs in archiveLogs.Batch(batchSize))
        {
            var batchLogs = logs.ToList();
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
                Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
                try
                {
                    transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "BeginTransactionAsync failed or was ignored by provider; proceeding without a transaction.");
                }
                var existingLogs = await GetExistingLogsAsync(context, batchLogs, cancellationToken);
                if (existingLogs.Count != 0)
                {
                    _logger.LogInformation(
                        "Replacing {Count} existing event windows before insert for {Start} through {End}.",
                        existingLogs.Count,
                        batchLogs.Min(log => log.Start),
                        batchLogs.Max(log => log.End));
                    context.IndiannaEvents.RemoveRange(existingLogs);
                    await context.SaveChangesAsync(cancellationToken);
                }

                context.IndiannaEvents.AddRange(batchLogs);
                await context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                _logger.LogInformation("Inserted event batch {BatchNumber} with {Count} records for {Date}", batchNumber, batchLogs.Count, batchLogs.First().Start);
                batchNumber++;
            });
        }
    }

    private static async Task<List<CompressedEventLogs<IndianaEvent>>> GetExistingLogsAsync(
        EventLogContext context,
        IReadOnlyCollection<CompressedEventLogs<IndianaEvent>> logs,
        CancellationToken cancellationToken)
    {
        var logKeys = logs.Select(CreateKey).ToHashSet();
        var locationIdentifiers = logKeys.Select(key => key.LocationIdentifier).Distinct().ToList();
        var deviceIds = logKeys.Select(key => key.DeviceId).Distinct().ToList();
        var starts = logKeys.Select(key => key.Start).Distinct().ToList();

        var existingLogs = await context.IndiannaEvents
                .Where(log =>
                    locationIdentifiers.Contains(log.LocationIdentifier) &&
                    deviceIds.Contains(log.DeviceId) &&
                    starts.Contains(log.Start))
                .ToListAsync(cancellationToken);

        return existingLogs.Where(log => logKeys.Contains(CreateKey(log))).ToList();
    }

    private static LogKey CreateKey(CompressedEventLogs<IndianaEvent> log) =>
        new(log.LocationIdentifier, log.DeviceId, log.Start);

    private List<Location> LoadCurrentLocations(DeviceTypes? requiredDeviceType, string? locationIdentifiers)
    {
        var locationsQuery = _locationRepository.GetList()
            .Include(location => location.Devices)
            .AsQueryable();

        if (requiredDeviceType.HasValue)
        {
            locationsQuery = locationsQuery
                .Where(location => location.Devices.Any(device => device.DeviceType == requiredDeviceType.Value));
        }

        if (!string.IsNullOrWhiteSpace(locationIdentifiers))
        {
            var allowedIdentifiers = locationIdentifiers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            locationsQuery = locationsQuery
                .Where(location => allowedIdentifiers.Contains(location.LocationIdentifier));
        }

        return locationsQuery
            .FromSpecification(new ActiveLocationSpecification())
            .GroupBy(location => location.LocationIdentifier)
            .Select(group => group.OrderByDescending(location => location.Start).FirstOrDefault()!)
            .ToList();
    }
}



