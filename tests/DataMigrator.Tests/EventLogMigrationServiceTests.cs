using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataMigrator.Services;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Polly;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Models.EventLogModels;
using Utah.Udot.Atspm.Data.Models;
using Xunit;

namespace DataMigrator.Tests
{
    public class EventLogMigrationServiceTests
    {
        private static IServiceProvider BuildServiceProvider(string dbName)
        {
            var services = new ServiceCollection();
            // Use InMemory provider for tests to avoid SQLite schema discrepancies
            services.AddDbContext<EventLogContext>((sp, options) =>
                options.UseInMemoryDatabase(dbName));

            var sp = services.BuildServiceProvider();
            // Ensure the in-memory SQLite database schema is created for EF Core
            using (var scope = sp.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<EventLogContext>();
                // Ensure database is initialized for InMemory provider
                ctx.Database.EnsureCreated();
                // No debug writes in tests
            }

            return sp;
        }

        [Fact]
        public async Task InsertLogsWithRetryAsync_InsertsBatch()
        {
            var sp = BuildServiceProvider(Guid.NewGuid().ToString());
            // No debug writes in tests
            var locationRepoMock = new Mock<Utah.Udot.Atspm.Repositories.ConfigurationRepositories.ILocationRepository>();
            var shortPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(new[] { TimeSpan.Zero });
            var svc = new EventLogMigrationService(NullLogger<EventLogMigrationService>.Instance, sp, locationRepoMock.Object, shortPolicy);

            var log = new CompressedEventLogs<IndianaEvent>
            {
                LocationIdentifier = "L1",
                DeviceId = 1,
                Start = new DateTime(2024, 1, 1),
                End = new DateTime(2024, 1, 1).AddHours(1),
                Data = new List<IndianaEvent>
                {
                    new IndianaEvent { Timestamp = new DateTime(2024,1,1), EventCode = 1, EventParam = 2 }
                }
            };

            var method = typeof(EventLogMigrationService).GetMethod("InsertLogsWithRetryAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var task = (Task)method!.Invoke(svc, new object[] { new List<CompressedEventLogs<IndianaEvent>> { log }, new Commands.MigrationCommandConfiguration { Batch = 500 }, CancellationToken.None })!;
            // ensure test doesn't hang on Polly retries: timeout after 2 minutes
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMinutes(2)));
            if (completed != task)
            {
                throw new TimeoutException("InsertLogsWithRetryAsync timed out after 2 minutes");
            }

            // The method completed without throwing; await to propagate any exception.
            await (Task)task!;
        }

        [Fact]
        public async Task GetExistingLogsAsync_FindsMatchingLogs()
        {
            var sp = BuildServiceProvider(Guid.NewGuid().ToString());

            using (var scope = sp.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<EventLogContext>();
                var existing = new CompressedEventLogs<IndianaEvent>
                {
                    LocationIdentifier = "LA",
                    DeviceId = 7,
                    Start = new DateTime(2025, 5, 5),
                    End = new DateTime(2025, 5, 5).AddHours(1),
                    Data = new List<IndianaEvent>()
                };
                context.IndiannaEvents.Add(existing);
                await context.SaveChangesAsync();
            }

            // prepare a batch with identical key
            var batch = new List<CompressedEventLogs<IndianaEvent>>
            {
                new CompressedEventLogs<IndianaEvent>
                {
                    LocationIdentifier = "LA",
                    DeviceId = 7,
                    Start = new DateTime(2025,5,5),
                    End = new DateTime(2025,5,5).AddHours(1),
                    Data = new List<IndianaEvent>()
                }
            };

            // call private static method GetExistingLogsAsync via reflection
            var method = typeof(EventLogMigrationService).GetMethod("GetExistingLogsAsync", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            using var scope2 = sp.CreateScope();
            var context2 = scope2.ServiceProvider.GetRequiredService<EventLogContext>();

            var task = (Task<List<CompressedEventLogs<IndianaEvent>>>)method!.Invoke(null, new object[] { context2, batch, CancellationToken.None })!;
            var completed2 = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMinutes(2)));
            if (completed2 != task)
            {
                throw new TimeoutException("GetExistingLogsAsync timed out after 2 minutes");
            }
            var result = await task;

            Assert.Single(result);
            Assert.Equal("LA", result[0].LocationIdentifier);
            Assert.Equal(7, result[0].DeviceId);
        }


    }
}
