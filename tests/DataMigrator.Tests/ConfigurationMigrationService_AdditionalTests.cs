using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataMigrator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Xunit;

namespace DataMigrator.Tests
{
    public class ConfigurationMigrationService_AdditionalTests
    {
        [Fact]
        public void PostgreSqlCompatibilityBridge_DoesNotModifyMigrationHistory()
        {
            Assert.DoesNotContain("__EFMigrationsHistory", ConfigurationMigrationService.PostgreSqlConfigCompatibilitySql, StringComparison.Ordinal);
            Assert.DoesNotContain("ProductVersion", ConfigurationMigrationService.PostgreSqlConfigCompatibilitySql, StringComparison.Ordinal);
        }

        [Fact]
        public void WireDetectionTypes_AppliesBaselineAndMappedTypes()
        {
            var first = new Detector { Id = 1 };
            var second = new Detector { Id = 2 };
            var baseline = new DetectionType { Id = DetectionTypes.B };
            var mapped = new DetectionType { Id = DetectionTypes.AC };
            var mappings = new List<DetectionTypeDetector>
            {
                new() { DetectionTypesId = (int)DetectionTypes.AC, DetectorsId = second.Id }
            };
            var method = typeof(ConfigurationMigrationService).GetMethod("WireDetectionTypes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            method!.Invoke(null, new object[]
            {
                new List<Detector> { first, second },
                new List<DetectionType> { baseline, mapped },
                mappings
            });

            Assert.Equal(new[] { 1, 2 }, baseline.Detectors.Select(detector => detector.Id).OrderBy(id => id));
            Assert.Equal(new[] { 2 }, mapped.Detectors.Select(detector => detector.Id));
        }

        [Fact]
        public void SetDetectionTypeMeasureType_AddsMeasureTypesPerDetectionType()
        {
            var detectionTypes = new List<DetectionType>
            {
                new DetectionType { Id = DetectionTypes.B },
                new DetectionType { Id = DetectionTypes.AC },
                new DetectionType { Id = DetectionTypes.AS },
                new DetectionType { Id = DetectionTypes.LLC },
                new DetectionType { Id = DetectionTypes.LLS },
                new DetectionType { Id = DetectionTypes.SBP }
            };

            var measureTypes = new List<MeasureType>
            {
                new MeasureType { Id = 1 }, new MeasureType { Id = 2 }, new MeasureType { Id = 3 }, new MeasureType { Id = 4 },
                new MeasureType { Id = 5 }, new MeasureType { Id = 6 }, new MeasureType { Id = 7 }, new MeasureType { Id = 8 },
                new MeasureType { Id = 9 }, new MeasureType { Id = 10 }, new MeasureType { Id = 11 }, new MeasureType { Id = 12 },
                new MeasureType { Id = 13 }, new MeasureType { Id = 14 }, new MeasureType { Id = 15 }, new MeasureType { Id = 17 },
                new MeasureType { Id = 31 }, new MeasureType { Id = 32 }, new MeasureType { Id = 36 }
            };

            var detectionRepo = new Mock<IDetectionTypeRepository>();
            detectionRepo.Setup(r => r.GetList()).Returns(detectionTypes.AsQueryable());
            var measureRepo = new Mock<IMeasureTypeRepository>();
            measureRepo.Setup(r => r.GetList()).Returns(measureTypes.AsQueryable());

            // create no-op mocks for other dependencies
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
                detectionRepo.Object,
                measureRepo.Object,
                new Mock<IRouteRepository>().Object,
                new Mock<IRouteLocationsRepository>().Object,
                new Mock<IServiceProvider>().Object);

            // Call private method via reflection
            var method = typeof(ConfigurationMigrationService).GetMethod("SetDetectionTypeMesureType", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            method!.Invoke(svc, Array.Empty<object>());

            // Each detection type should have had measure types added (non-empty)
            foreach (var dt in detectionTypes)
            {
                Assert.NotEmpty(dt.MeasureTypes);
            }
        }

        [Fact]
        public void ImportProducts_SkipsWhenProductsExist()
        {
            var productRepo = new Mock<IProductRepository>();
            productRepo.Setup(r => r.GetList()).Returns(new List<Product> { new Product() }.AsQueryable());

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

            var method = typeof(ConfigurationMigrationService).GetMethod("ImportProducts", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            // call with minimal dictionaries; since products exist, method should return early
            method!.Invoke(svc, new object[] { new Dictionary<string, string>(), new Dictionary<string, Dictionary<string, string>>() });

            productRepo.Verify(r => r.AddRange(It.IsAny<IEnumerable<Product>>()), Times.Never);
        }

        [Fact]
        public void ImportProducts_CallsAddRangeWhenNoneAndSourceEmpty_UsesImportDataGuard()
        {
            var productRepo = new Mock<IProductRepository>();
            productRepo.Setup(r => r.GetList()).Returns(Enumerable.Empty<Product>().AsQueryable());
            productRepo.Setup(r => r.AddRange(It.IsAny<IEnumerable<Product>>()));

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

            // Set private _config field to an options instance with empty Source to avoid real SQL in ImportData
            var configField = typeof(ConfigurationMigrationService).GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(configField);
            configField!.SetValue(svc, new Commands.TransferConfigCommandConfiguration { Source = string.Empty });

            var method = typeof(ConfigurationMigrationService).GetMethod("ImportProducts", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            method!.Invoke(svc, new object[] { new Dictionary<string, string> { ["Products"] = "query" }, new Dictionary<string, Dictionary<string, string>> { ["Products"] = new Dictionary<string, string>() } });

            // Even though ImportData returns empty list due to empty Source, AddRange should be invoked (with empty list)
            productRepo.Verify(r => r.AddRange(It.IsAny<IEnumerable<Product>>()), Times.Once);
        }
    }
}

