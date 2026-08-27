using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DataMigrator.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Xunit;

namespace DataMigrator.Tests
{
    public class EventLogMigrationService_LoadCurrentLocationsTests
    {
        [Fact]
        public void LoadCurrentLocations_ReturnsLatestVersionPerIdentifier_AndFiltersByDeviceType()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var services = new ServiceCollection();
            services.AddDbContext<ConfigContext>(options => options.UseSqlite(connection));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ConfigContext>();
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

            var loc1v1 = new Location { LocationIdentifier = "L1", Start = new DateTime(2020, 1, 1), Devices = new List<Device> { CreateDevice("L1-v1", DeviceTypes.SignalController) } };
            var loc1v2 = new Location { LocationIdentifier = "L1", Start = new DateTime(2021, 1, 1), Devices = new List<Device> { CreateDevice("L1-v2", DeviceTypes.SignalController) } };
            var loc2 = new Location { LocationIdentifier = "L2", Start = new DateTime(2022, 1, 1), Devices = new List<Device> { CreateDevice("L2", (DeviceTypes)999) } };
            context.AddRange(loc1v1, loc1v2, loc2);
            context.SaveChanges();

            var repo = new Mock<ILocationRepository>();
            repo.Setup(r => r.GetList()).Returns(context.Set<Location>());

            var svc = new EventLogMigrationService(NullLogger<EventLogMigrationService>.Instance, provider, repo.Object);

            var method = typeof(EventLogMigrationService).GetMethod("LoadCurrentLocations", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var result = (IList<Location>)method!.Invoke(svc, new object?[] { (DeviceTypes?)DeviceTypes.SignalController, null })!;

            // Expect only L1 latest version
            Assert.Single(result);
            Assert.Equal("L1", result[0].LocationIdentifier);
            Assert.Equal(new DateTime(2021, 1, 1), result[0].Start);
        }

        [Fact]
        public void LoadCurrentLocations_FiltersByProvidedIdentifiers()
        {
            var locA = new Location { LocationIdentifier = "A", Start = new DateTime(2020, 1, 1), Devices = new List<Device> { new Device { DeviceType = DeviceTypes.SignalController } } };
            var locB = new Location { LocationIdentifier = "B", Start = new DateTime(2020, 1, 1), Devices = new List<Device> { new Device { DeviceType = DeviceTypes.SignalController } } };

            var repo = new Mock<ILocationRepository>();
            repo.Setup(r => r.GetList()).Returns(new List<Location> { locA, locB }.AsQueryable());

            var svc = new EventLogMigrationService(NullLogger<EventLogMigrationService>.Instance, new Mock<IServiceProvider>().Object, repo.Object);

            var method = typeof(EventLogMigrationService).GetMethod("LoadCurrentLocations", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var result = (IList<Location>)method!.Invoke(svc, new object?[] { null, "A" })!;

            Assert.Single(result);
            Assert.Equal("A", result[0].LocationIdentifier);
        }

        private static Device CreateDevice(string identifier, DeviceTypes deviceType) => new()
        {
            DeviceIdentifier = identifier,
            DeviceType = deviceType,
            Ipaddress = string.Empty
        };
    }
}
