#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Tests/SpeedEventMigrationServiceTests.cs
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

using DataMigrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Xunit;

namespace DataMigrator.Tests;

public sealed class SpeedEventMigrationServiceTests
{
    [Fact]
    public void BuildSourceConnectionString_NormalizesQuotedInputAndAppliesExpectedSettings()
    {
        const string rawConnectionString = "\"Server=sql01;Database=MOE;User Id=test;Password=secret;Connect Timeout=15;MultipleActiveResultSets=True;Pooling=True\"";

        var normalized = InvokeBuildSourceConnectionString(rawConnectionString);
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(normalized);

        Assert.Equal("sql01", builder.DataSource);
        Assert.Equal("MOE", builder.InitialCatalog);
        Assert.Equal("test", builder.UserID);
        Assert.False(builder.MultipleActiveResultSets);
        Assert.False(builder.Pooling);
        Assert.Equal("DataMigrator.SpeedMigration", builder.ApplicationName);
        Assert.True(builder.ConnectTimeout >= 60);
        Assert.DoesNotContain('"', normalized);
    }

    [Fact]
    public async Task GetExistingLogsAsync_MatchesStableKeyAndIgnoresEndDifference()
    {
        var serviceProvider = BuildServiceProvider();
        var existingStart = new DateTime(2026, 4, 10, 9, 0, 0);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
            context.SpeedEvents.AddRange(
                new CompressedEventLogs<SpeedEvent>
                {
                    LocationIdentifier = "5234",
                    DeviceId = 42,
                    Start = existingStart,
                    End = existingStart.AddMinutes(59),
                    Data = [new SpeedEvent { DetectorId = "523401", Mph = 35, Kph = 56, Timestamp = existingStart.AddMinutes(1) }]
                },
                new CompressedEventLogs<SpeedEvent>
                {
                    LocationIdentifier = "5234",
                    DeviceId = 42,
                    Start = existingStart.AddHours(1),
                    End = existingStart.AddHours(2),
                    Data = [new SpeedEvent { DetectorId = "523499", Mph = 30, Kph = 48, Timestamp = existingStart.AddHours(1).AddMinutes(1) }]
                });
            await context.SaveChangesAsync();
        }

        await using var verificationScope = serviceProvider.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<EventLogContext>();
        var candidate = new CompressedEventLogs<SpeedEvent>
        {
            LocationIdentifier = "5234",
            DeviceId = 42,
            Start = existingStart,
            End = existingStart.AddHours(1),
            Data =
            [
                new SpeedEvent { DetectorId = "523401", Mph = 40, Kph = 64, Timestamp = existingStart.AddMinutes(2) },
                new SpeedEvent { DetectorId = "523402", Mph = 41, Kph = 65, Timestamp = existingStart.AddMinutes(3) }
            ]
        };

        var matches = await InvokeGetExistingLogsAsync(verificationContext, [candidate]);

        var stored = Assert.Single(matches);
        Assert.Equal("5234", stored.LocationIdentifier);
        Assert.Equal(42, stored.DeviceId);
        Assert.Equal(existingStart, stored.Start);
        Assert.Equal(existingStart.AddMinutes(59), stored.End);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton(connection);
        services.AddDbContext<EventLogContext>((sp, options) => options.UseSqlite(sp.GetRequiredService<SqliteConnection>()));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
        context.Database.EnsureCreated();
        return provider;
    }

    private static string InvokeBuildSourceConnectionString(string value)
    {
        var method = typeof(SpeedEventMigrationService).GetMethod("BuildSourceConnectionString", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [value])!;
    }

    private static async Task<List<CompressedEventLogs<SpeedEvent>>> InvokeGetExistingLogsAsync(EventLogContext context, IReadOnlyCollection<CompressedEventLogs<SpeedEvent>> logs)
    {
        var method = typeof(SpeedEventMigrationService).GetMethod("GetExistingLogsAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var task = (Task<List<CompressedEventLogs<SpeedEvent>>>)method!.Invoke(null, [context, logs, CancellationToken.None])!;
        return await task;
    }
}
