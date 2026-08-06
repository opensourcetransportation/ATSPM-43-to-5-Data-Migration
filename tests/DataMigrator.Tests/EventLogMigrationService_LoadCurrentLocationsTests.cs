using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DataMigrator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
            var loc1v1 = new Location { LocationIdentifier = "L1", Start = new DateTime(2020, 1, 1), Devices = new List<Device> { new Device { DeviceType = DeviceTypes.SignalController } } };
            var loc1v2 = new Location { LocationIdentifier = "L1", Start = new DateTime(2021, 1, 1), Devices = new List<Device> { new Device { DeviceType = DeviceTypes.SignalController } } };
            var loc2 = new Location { LocationIdentifier = "L2", Start = new DateTime(2022, 1, 1), Devices = new List<Device> { new Device { DeviceType = (DeviceTypes)999 } } };

            var repo = new Mock<ILocationRepository>();
            repo.Setup(r => r.GetList()).Returns(new List<Location> { loc1v1, loc1v2, loc2 }.AsQueryable());

            var svc = new EventLogMigrationService(NullLogger<EventLogMigrationService>.Instance, new Mock<IServiceProvider>().Object, repo.Object);

            var method = typeof(EventLogMigrationService).GetMethod("LoadCurrentLocations", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var result = (IList<Location>)method!.Invoke(svc, new object[] { (DeviceTypes?)DeviceTypes.SignalController, null })!;

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

            var result = (IList<Location>)method!.Invoke(svc, new object[] { null, "A" })!;

            Assert.Single(result);
            Assert.Equal("A", result[0].LocationIdentifier);
        }
    }
}
