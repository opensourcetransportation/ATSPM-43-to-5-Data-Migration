using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DataMigrator.Services;
using Utah.Udot.Atspm.Data.Models;
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Repositories.ConfigurationRepositories;
using Xunit;

namespace DataMigrator.Tests
{
    public class SpeedEventMigrationService_MethodsTests
    {
        [Fact]
        public void GetSourceDetectorIdentifiers_ReturnsDistinctOrderedIdentifiers()
        {
            var detectors = new List<Detector>
            {
                new Detector { DectectorIdentifier = "b" },
                new Detector { DectectorIdentifier = "a" },
                new Detector { DectectorIdentifier = "A" },
                new Detector { DectectorIdentifier = "" },
                new Detector { DectectorIdentifier = "c" }
            };

            var loc = new Location { Approaches = new List<Approach> { new Approach { Detectors = detectors } } };

            var method = typeof(SpeedEventMigrationService).GetMethod("GetSourceDetectorIdentifiers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var result = (List<string>)method!.Invoke(null, new object[] { loc })!;

            Assert.Equal(new List<string> { "a", "b", "c" }.OrderBy(s => s, StringComparer.OrdinalIgnoreCase), result.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            Assert.DoesNotContain(string.Empty, result);
        }

        [Fact]
        public void BuildSpeedEventQuery_IncludesDetectorParameters()
        {
            var method = typeof(SpeedEventMigrationService).GetMethod("BuildSpeedEventQuery", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var query = (string)method!.Invoke(null, new object[] { 3 })!;

            Assert.Contains("@detectorId0", query);
            Assert.Contains("@detectorId1", query);
            Assert.Contains("@detectorId2", query);
            Assert.Contains("OPTION (RECOMPILE)", query, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FROM [dbo].[Speed_Events]", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MOE", query, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("INDEX(", query, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildSpeedEventQuery_RejectsMoreThanSqlServerSafeDetectorLimit()
        {
            var method = typeof(SpeedEventMigrationService).GetMethod("BuildSpeedEventQuery", BindingFlags.NonPublic | BindingFlags.Static);

            var exception = Assert.Throws<TargetInvocationException>(() =>
                method!.Invoke(null, new object[] { SpeedEventMigrationService.MaxDetectorParametersPerQuery + 1 }));

            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        }

        [Fact]
        public void ChunkDetectorIdentifiers_StaysBelowSqlServerParameterLimit()
        {
            var detectorIds = Enumerable.Range(0, 4501).Select(index => index.ToString()).ToArray();

            var chunks = SpeedEventMigrationService.ChunkDetectorIdentifiers(detectorIds).ToList();

            Assert.Equal(3, chunks.Count);
            Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, SpeedEventMigrationService.MaxDetectorParametersPerQuery));
            Assert.Equal(detectorIds, chunks.SelectMany(chunk => chunk));
        }

        [Fact]
        public void BuildSourceConnectionString_NormalizesQuotesAndTimeout()
        {
            var method = typeof(SpeedEventMigrationService).GetMethod("BuildSourceConnectionString", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var raw = "\"Server=.;Integrated Security=true;Connect Timeout=5\"";
            var result = (string)method!.Invoke(null, new object[] { raw })!;

            Assert.Contains("Application Name=DataMigrator.SpeedMigration", result);
            Assert.Contains("Connect Timeout=60", result);
        }

        [Fact]
        public void LoadCurrentLocations_FiltersToSpeedSensorsAndLatestVersions()
        {
            var loc1v1 = new Location { LocationIdentifier = "L1", Start = new DateTime(2020, 1, 1), Devices = new List<Device> { new Device { DeviceType = DeviceTypes.SpeedSensor } } };
            var loc1v2 = new Location { LocationIdentifier = "L1", Start = new DateTime(2021, 1, 1), Devices = new List<Device> { new Device { DeviceType = DeviceTypes.SpeedSensor } } };
            var loc2 = new Location { LocationIdentifier = "L2", Start = new DateTime(2022, 1, 1), Devices = new List<Device> { new Device { DeviceType = (DeviceTypes)999 } } };

            var repo = new Mock<ILocationRepository>();
            repo.Setup(r => r.GetList()).Returns(new List<Location> { loc1v1, loc1v2, loc2 }.AsQueryable());

            var svc = new SpeedEventMigrationService(NullLogger<SpeedEventMigrationService>.Instance, new Mock<IServiceProvider>().Object, repo.Object);

            var method = typeof(SpeedEventMigrationService).GetMethod("LoadCurrentLocations", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var result = (IList<Location>)method!.Invoke(svc, new object[] { null })!;

            Assert.Single(result);
            Assert.Equal("L1", result[0].LocationIdentifier);
            Assert.Equal(new DateTime(2021, 1, 1), result[0].Start);
        }
    }
}
