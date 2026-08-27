using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moq;
using DataMigrator.Services;
using Utah.Udot.Atspm.Data;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Xunit;

namespace DataMigrator.Tests
{
    public class ConfigurationMigrationService_MethodsTests
    {
        [Fact]
        public void CreateSourceCommand_UsesExtendedTimeout()
        {
            using var connection = new SqlConnection();
            using var command = ConfigurationMigrationService.CreateSourceCommand("SELECT 1", connection);

            Assert.Equal(300, command.CommandTimeout);
        }

        [Fact]
        public void DefaultLocationQueries_DoNotHardcodeSourceDatabaseName()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
                .Build();
            var queries = configuration.GetSection("LocationQueries").GetChildren();

            Assert.NotEmpty(queries);
            Assert.All(queries, query => Assert.DoesNotContain("[MOE]", query.Value, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void GetColumnMappings_ReturnsMappingsFromConfiguration()
        {
            var settings = new Dictionary<string, string>
            {
                ["ColumnMappings:Products:Id"] = "Id",
                ["ColumnMappings:Products:Manufacturer"] = "Manufacturer",
                ["ColumnMappings:Devices:DeviceIdentifier"] = "DeviceIdentifier"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            var mappings = ConfigurationMigrationService.GetColumnMappings(config);

            Assert.True(mappings.ContainsKey("Products"));
            Assert.Equal("Id", mappings["Products"]["Id"]);
            Assert.Equal("Manufacturer", mappings["Products"]["Manufacturer"]);
            Assert.True(mappings.ContainsKey("Devices"));
        }

        [Fact]
        public void GetColumnMappings_ThrowsWhenSectionMissing()
        {
            var config = new ConfigurationBuilder().Build();
            Assert.Throws<KeyNotFoundException>(() => ConfigurationMigrationService.GetColumnMappings(config));
        }

        [Fact]
        public void GetLocationQueries_ReturnsQueries()
        {
            var settings = new Dictionary<string, string>
            {
                ["LocationQueries:Locations"] = "select * from Locations",
                ["LocationQueries:AreaLocations"] = "select * from AreaLocations"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

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

            var method = typeof(ConfigurationMigrationService).GetMethod("GetLocationQueries", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var result = (Dictionary<string, string>)method!.Invoke(svc, new object[] { config })!;

            Assert.Equal("select * from Locations", result["Locations"]);
            Assert.Equal("select * from AreaLocations", result["AreaLocations"]);
        }

        [Fact]
        public void ImportData_SkipsWhenSourceEmpty_ReturnsEmptyList()
        {
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

            // Ensure _config.Source is empty to trigger guard
            var configField = typeof(ConfigurationMigrationService).GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance);
            configField!.SetValue(svc, new Commands.TransferConfigCommandConfiguration { Source = string.Empty });

            var method = typeof(ConfigurationMigrationService).GetMethod("ImportData", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var generic = method!.MakeGenericMethod(typeof(Product));
            var result = (IList<Product>)generic.Invoke(svc, new object[] { "select 1", new Dictionary<string, string>() })!;

            Assert.Empty(result);
        }

        [Fact]
        public void ResetSequences_SkipsWhenNotPostgres()
        {
            // Build a real service provider with an InMemory ConfigContext so the CreateScope() extension works
            var services = new ServiceCollection();
            services.AddDbContext<ConfigContext>(opt => opt.UseInMemoryDatabase("seqtest"));
            var provider = services.BuildServiceProvider();

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
                provider);

            var method = typeof(ConfigurationMigrationService).GetMethod("ResetSequences", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            // Should not throw when provider is not Npgsql
            method!.Invoke(svc, Array.Empty<object>());
        }

        [Fact]
        public void DeleteProducts_CallsRemoveRange()
        {
            var products = new List<Product> { new Product { Id = 1 } };
            var productRepo = new Mock<IProductRepository>();
            productRepo.Setup(r => r.GetList()).Returns(products.AsQueryable());
            productRepo.Setup(r => r.RemoveRange(It.IsAny<List<Product>>()));

            var svc = new ConfigurationMigrationService(
                NullLogger<ConfigurationMigrationService>.Instance,
                new Mock<IJurisdictionRepository>().Object,
                new Mock<ILocationTypeRepository>().Object,
                new Mock<ILocationRepository>().Object,
                new Mock<IApproachRepository>().Object,
                new Mock<IDetectorRepository>().Object,
                new Mock<IDeviceRepository>().Object,
                new Mock<IDeviceConfigurationRepository>().Object,
                productRepo.Object,
                new Mock<IRegionsRepository>().Object,
                new Mock<IAreaRepository>().Object,
                new Mock<IDetectionTypeRepository>().Object,
                new Mock<IMeasureTypeRepository>().Object,
                new Mock<IRouteRepository>().Object,
                new Mock<IRouteLocationsRepository>().Object,
                new Mock<IServiceProvider>().Object);

            var method = typeof(ConfigurationMigrationService).GetMethod("DeleteProducts", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            method!.Invoke(svc, Array.Empty<object>());

            productRepo.Verify(r => r.RemoveRange(It.Is<List<Product>>(l => l.Count == 1)), Times.Once);
        }

        [Fact]
        public void DeleteLocations_CallsRemoveRange()
        {
            var locations = new List<Location> { new Location { Id = 5 } };
            var locationRepo = new Mock<ILocationRepository>();
            locationRepo.Setup(r => r.GetList()).Returns(locations.AsQueryable());
            locationRepo.Setup(r => r.RemoveRange(It.IsAny<List<Location>>()));

            var svc = new ConfigurationMigrationService(
                NullLogger<ConfigurationMigrationService>.Instance,
                new Mock<IJurisdictionRepository>().Object,
                new Mock<ILocationTypeRepository>().Object,
                locationRepo.Object,
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

            var method = typeof(ConfigurationMigrationService).GetMethod("DeleteLocations", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            method!.Invoke(svc, Array.Empty<object>());

            locationRepo.Verify(r => r.RemoveRange(It.Is<List<Location>>(l => l.Count == 1)), Times.Once);
        }

        [Fact]
        public void ImportDeviceConfigurations_CallsAddRangeWhenRepositoryEmpty()
        {
            var repo = new Mock<IDeviceConfigurationRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<DeviceConfiguration>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<DeviceConfiguration>>()));

            var svc = BuildConfigurationService(deviceConfigurationRepository: repo.Object);
            SetEmptySourceConfig(svc);

            InvokePrivate(svc, "ImportDeviceConfigurations", new object[] { new Dictionary<string, string> { ["DeviceConfigurations"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["DeviceConfigurations"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.AddRange(It.IsAny<IEnumerable<DeviceConfiguration>>()), Times.Once);
        }

        [Fact]
        public void ImportAreas_CallsAddRangeWhenRepositoryEmpty()
        {
            var repo = new Mock<IAreaRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Area>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Area>>()));

            var svc = BuildConfigurationService(areaRepository: repo.Object);
            SetEmptySourceConfig(svc);
            InvokePrivate(svc, "ImportAreas", new object[] { new Dictionary<string, string> { ["Areas"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Areas"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.AddRange(It.IsAny<IEnumerable<Area>>()), Times.Once);
        }

        [Fact]
        public void ImportRegions_CallsAddRangeWhenRepositoryEmpty()
        {
            var repo = new Mock<IRegionsRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Region>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Region>>()));

            var svc = BuildConfigurationService(regionsRepository: repo.Object);
            SetEmptySourceConfig(svc);
            InvokePrivate(svc, "ImportRegions", new object[] { new Dictionary<string, string> { ["Regions"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Regions"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.AddRange(It.IsAny<IEnumerable<Region>>()), Times.Once);
        }

        [Fact]
        public void ImportRoutes_CallsAddRangeWhenRepositoryEmpty()
        {
            var repo = new Mock<IRouteRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Route>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Route>>()));

            var svc = BuildConfigurationService(routeRepository: repo.Object);
            SetEmptySourceConfig(svc);
            InvokePrivate(svc, "ImportRoutes", new object[] { new Dictionary<string, string> { ["Routes"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Routes"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.AddRange(It.IsAny<IEnumerable<Route>>()), Times.Once);
        }

        [Fact]
        public void ImportRouteLocations_CallsAddRangeWhenRepositoryEmpty()
        {
            var repo = new Mock<IRouteLocationsRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<RouteLocation>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<RouteLocation>>()));

            var svc = BuildConfigurationService(routeLocationsRepository: repo.Object);
            SetEmptySourceConfig(svc);
            InvokePrivate(svc, "ImportRouteLocations", new object[] { new Dictionary<string, string> { ["RouteLocations"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["RouteLocations"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.AddRange(It.IsAny<IEnumerable<RouteLocation>>()), Times.Once);
        }

        [Fact]
        public void ImportLocations_CallsAddRangeWhenRepositoryEmpty()
        {
            var repo = new Mock<ILocationRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Location>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Location>>()));
            repo.Setup(r => r.GetLatestVersionOfLocation(It.IsAny<string>())).Returns<Location?>(null);

            var svc = BuildConfigurationService(locationRepository: repo.Object);
            SetEmptySourceConfig(svc, delete: true);
            InvokePrivate(svc, "ImportLocations", new object[] { new Dictionary<string, string> { ["Locations"] = "query", ["AreaLocations"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Locations"] = new Dictionary<string, string>(), ["AreaLocations"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.AddRange(It.IsAny<IEnumerable<Location>>()), Times.Once);
        }

        [Fact]
        public void ImportDetectors_DoesNotThrowWhenSourceEmpty()
        {
            var repo = new Mock<IDetectorRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Detector>().AsQueryable());
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Detector>>()));
            repo.Setup(r => r.Add(It.IsAny<Detector>()));

            var detectionTypeRepo = new Mock<IDetectionTypeRepository>();
            detectionTypeRepo.Setup(r => r.GetList()).Returns(new List<DetectionType> { new DetectionType { Id = DetectionTypes.B } }.AsQueryable());

            var svc = BuildConfigurationService(detectorRepository: repo.Object, detectionTypeRepository: detectionTypeRepo.Object);
            SetEmptySourceConfig(svc, delete: true);

            var exception = Record.Exception(() => InvokePrivate(svc, "ImportDetectors", new object[] { new Dictionary<string, string> { ["Detectors"] = "query", ["DetectionTypeDetector"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Detectors"] = new Dictionary<string, string>(), ["DetectionTypeDetector"] = new Dictionary<string, string>() } }));

            Assert.Null(exception);
        }

        [Fact]
        public void ImportApproaches_DoesNotThrowWhenSourceEmpty()
        {
            var repo = new Mock<IApproachRepository>();
            repo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Approach>().AsQueryable());
            repo.Setup(r => r.Add(It.IsAny<Approach>()));
            repo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Approach>>()));

            var svc = BuildConfigurationService(approachRepository: repo.Object);
            SetEmptySourceConfig(svc);
            InvokePrivate(svc, "ImportApproaches", new object[] { new Dictionary<string, string> { ["Approaches"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Approaches"] = new Dictionary<string, string>() } });

            repo.Verify(r => r.Add(It.IsAny<Approach>()), Times.Never);
        }

        [Fact]
        public void ImportSpeedDevices_AddsSpeedConfigurationWhenProductsMissing()
        {
            var products = new List<Product>();
            var productRepo = new Mock<IProductRepository>();
            productRepo.Setup(r => r.GetList()).Returns(() => products.AsQueryable());
            productRepo.Setup(r => r.Add(It.IsAny<Product>())).Callback<Product>(p => products.Add(p));

            var deviceConfigurations = new List<DeviceConfiguration>();
            var deviceConfigurationRepo = new Mock<IDeviceConfigurationRepository>();
            deviceConfigurationRepo.Setup(r => r.GetList()).Returns(() => deviceConfigurations.AsQueryable());
            deviceConfigurationRepo.Setup(r => r.Add(It.IsAny<DeviceConfiguration>())).Callback<DeviceConfiguration>(dc => deviceConfigurations.Add(dc));

            var locationRepo = new Mock<ILocationRepository>();
            locationRepo.Setup(r => r.GetLatestVersionOfLocation(It.IsAny<string>())).Returns<Location?>(null);

            var deviceRepo = new Mock<IDeviceRepository>();
            deviceRepo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Device>().AsQueryable());

            var svc = BuildConfigurationService(
                productRepository: productRepo.Object,
                deviceConfigurationRepository: deviceConfigurationRepo.Object,
                locationRepository: locationRepo.Object,
                deviceRepository: deviceRepo.Object);
            SetEmptySourceConfig(svc);
            InvokePrivate(svc, "ImportSpeedDevices", new object[] { new Dictionary<string, string> { ["SpeedDevices"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["SpeedDevices"] = new Dictionary<string, string>() } });

            productRepo.Verify(r => r.Add(It.Is<Product>(p => p.Manufacturer == "Wavetronix")), Times.Once);
            deviceConfigurationRepo.Verify(r => r.Add(It.Is<DeviceConfiguration>(dc => dc.Description == "Speed")), Times.Once);
        }

        private static ConfigurationMigrationService BuildConfigurationService(
            IJurisdictionRepository? jurisdictionRepository = null,
            ILocationTypeRepository? locationTypeRepository = null,
            ILocationRepository? locationRepository = null,
            IApproachRepository? approachRepository = null,
            IDetectorRepository? detectorRepository = null,
            IDeviceRepository? deviceRepository = null,
            IDeviceConfigurationRepository? deviceConfigurationRepository = null,
            IProductRepository? productRepository = null,
            IRegionsRepository? regionsRepository = null,
            IAreaRepository? areaRepository = null,
            IDetectionTypeRepository? detectionTypeRepository = null,
            IMeasureTypeRepository? measureTypeRepository = null,
            IRouteRepository? routeRepository = null,
            IRouteLocationsRepository? routeLocationsRepository = null,
            IServiceProvider? serviceProvider = null)
        {
            return new ConfigurationMigrationService(
                NullLogger<ConfigurationMigrationService>.Instance,
                jurisdictionRepository ?? new Mock<IJurisdictionRepository>().Object,
                locationTypeRepository ?? new Mock<ILocationTypeRepository>().Object,
                locationRepository ?? new Mock<ILocationRepository>().Object,
                approachRepository ?? new Mock<IApproachRepository>().Object,
                detectorRepository ?? new Mock<IDetectorRepository>().Object,
                deviceRepository ?? new Mock<IDeviceRepository>().Object,
                deviceConfigurationRepository ?? new Mock<IDeviceConfigurationRepository>().Object,
                productRepository ?? new Mock<IProductRepository>().Object,
                regionsRepository ?? new Mock<IRegionsRepository>().Object,
                areaRepository ?? new Mock<IAreaRepository>().Object,
                detectionTypeRepository ?? new Mock<IDetectionTypeRepository>().Object,
                measureTypeRepository ?? new Mock<IMeasureTypeRepository>().Object,
                routeRepository ?? new Mock<IRouteRepository>().Object,
                routeLocationsRepository ?? new Mock<IRouteLocationsRepository>().Object,
                serviceProvider ?? new Mock<IServiceProvider>().Object);
        }

        private static void SetEmptySourceConfig(ConfigurationMigrationService svc, bool delete = false)
        {
            var configField = typeof(ConfigurationMigrationService).GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(configField);
            configField!.SetValue(svc, new Commands.TransferConfigCommandConfiguration { Source = string.Empty, Delete = delete });
        }

        private static void InvokePrivate(ConfigurationMigrationService svc, string methodName, object[] parameters)
        {
            var method = typeof(ConfigurationMigrationService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            method!.Invoke(svc, parameters);
        }
    }
}
