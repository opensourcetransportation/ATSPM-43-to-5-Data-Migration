using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataMigrator.Commands;
using DataMigrator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;

//
namespace DataMigrator.Tests
{
    public class ConfigurationMigrationServiceTests
    {
        [Fact]
        public async Task RunAsync_WithDeleteTrue_CallsRemoveRangeOnRepositories()
        {
            var jurisdictionRepo = new Mock<IJurisdictionRepository>();
            jurisdictionRepo.Setup(r => r.GetList()).Returns(new List<Jurisdiction> { new Jurisdiction() }.AsQueryable());
            var locationTypeRepo = new Mock<ILocationTypeRepository>();
            var locationRepo = new Mock<ILocationRepository>();
            locationRepo.Setup(r => r.GetList()).Returns(new List<Location> { new Location() }.AsQueryable());
            var approachRepo = new Mock<IApproachRepository>();
            approachRepo.Setup(r => r.GetList()).Returns(new List<Approach> { new Approach() }.AsQueryable());
            var detectorRepo = new Mock<IDetectorRepository>();
            detectorRepo.Setup(r => r.GetList()).Returns(new List<Detector> { new Detector() }.AsQueryable());
            var deviceRepo = new Mock<IDeviceRepository>();
            deviceRepo.Setup(r => r.GetList()).Returns(new List<Device> { new Device() }.AsQueryable());
            var deviceConfigurationRepo = new Mock<IDeviceConfigurationRepository>();
            deviceConfigurationRepo.Setup(r => r.GetList()).Returns(new List<DeviceConfiguration> { new DeviceConfiguration() }.AsQueryable());
            var productRepo = new Mock<IProductRepository>();
            productRepo.Setup(r => r.GetList()).Returns(new List<Product> { new Product() }.AsQueryable());
            var regionsRepo = new Mock<IRegionsRepository>();
            regionsRepo.Setup(r => r.GetList()).Returns(new List<Region> { new Region() }.AsQueryable());
            var areaRepo = new Mock<IAreaRepository>();
            areaRepo.Setup(r => r.GetList()).Returns(new List<Area> { new Area() }.AsQueryable());
            var detectionTypeRepo = new Mock<IDetectionTypeRepository>();
            detectionTypeRepo.Setup(r => r.GetList()).Returns(new List<DetectionType> { new DetectionType() }.AsQueryable());
            var measureTypeRepo = new Mock<IMeasureTypeRepository>();
            measureTypeRepo.Setup(r => r.GetList()).Returns(new List<MeasureType> { new MeasureType() }.AsQueryable());
            var routeRepo = new Mock<IRouteRepository>();
            routeRepo.Setup(r => r.GetList()).Returns(new List<Route> { new Route() }.AsQueryable());
            var routeLocationsRepo = new Mock<IRouteLocationsRepository>();
            routeLocationsRepo.Setup(r => r.GetList()).Returns(new List<RouteLocation> { new RouteLocation() }.AsQueryable());

            var serviceProvider = new Mock<IServiceProvider>();

            var svc = new ConfigurationMigrationService(
                NullLogger<ConfigurationMigrationService>.Instance,
                jurisdictionRepo.Object,
                locationTypeRepo.Object,
                locationRepo.Object,
                approachRepo.Object,
                detectorRepo.Object,
                deviceRepo.Object,
                deviceConfigurationRepo.Object,
                productRepo.Object,
                regionsRepo.Object,
                areaRepo.Object,
                detectionTypeRepo.Object,
                measureTypeRepo.Object,
                routeRepo.Object,
                routeLocationsRepo.Object,
                serviceProvider.Object);

            var config = new TransferConfigCommandConfiguration { Delete = true, UpdateLocations = false, ImportSpeedDevices = false };

            // Call the private delete methods directly to avoid external dependencies
            void InvokePrivate(string name)
            {
                var method = typeof(ConfigurationMigrationService).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(method);
                method!.Invoke(svc, Array.Empty<object>());
            }

            InvokePrivate("DeleteLocations");
            InvokePrivate("DeleteRegions");
            InvokePrivate("DeleteAreas");
            InvokePrivate("DeleteJurisdictions");
            InvokePrivate("DeleteDevices");
            InvokePrivate("DeleteDevicesConfigurations");
            InvokePrivate("DeleteProducts");

            jurisdictionRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.Jurisdiction>>()), Times.Once);
            locationRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.Location>>()), Times.Once);
            regionsRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.Region>>()), Times.Once);
            areaRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.Area>>()), Times.Once);
            productRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.Product>>()), Times.Once);
            deviceConfigurationRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.DeviceConfiguration>>()), Times.Once);
            deviceRepo.Verify(r => r.RemoveRange(It.IsAny<IEnumerable<Utah.Udot.Atspm.Data.Models.Device>>()), Times.Once);
        }

        [Fact]
        public void GetColumnMappings_ThrowsWhenMissing()
        {
            var emptyConfig = new ConfigurationBuilder().Build();
            Assert.Throws<KeyNotFoundException>(() => ConfigurationMigrationService.GetColumnMappings(emptyConfig));
        }

        [Fact]
        public void Private_GetLocationQueries_ReturnsValues()
        {
            // Arrange
            var inMemory = new Dictionary<string, string>
            {
                { "LocationQueries:Foo", "select 1" },
                { "LocationQueries:Bar", "select 2" }
            };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

            var svc = new ConfigurationMigrationService(
                NullLogger<ConfigurationMigrationService>.Instance,
                new Mock<IJurisdictionRepository>().Object,
                new Mock<ILocationTypeRepository>().Object,
                new Mock<ILocationRepository>().Object,
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

            var method = typeof(ConfigurationMigrationService).GetMethod("GetLocationQueries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);

            var result = method!.Invoke(svc, new object[] { configuration }) as Dictionary<string, string>;

            Assert.NotNull(result);
            Assert.Equal("select 1", result!["Foo"]);
            Assert.Equal("select 2", result["Bar"]);
        }
    }
}
