using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DataMigrator.Commands;
using DataMigrator.Services;
using Xunit;

namespace DataMigrator.Tests
{
    public class HostedServices_StopAsyncTests
    {
        [Fact]
        public async Task TransferConfigCommandHostedService_StopAsync_Completes()
        {
            var svc = new TransferConfigCommandHostedService(new Mock<IConfigurationMigrationService>().Object, Options.Create(new TransferConfigCommandConfiguration()));
            await svc.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task TransferEventLogsHostedService_StopAsync_Completes()
        {
            var svc = new TransferEventLogsHostedService(new Mock<IEventLogMigrationService>().Object, Options.Create(new MigrationCommandConfiguration()));
            await svc.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task TransferSpeedEventsHostedService_StopAsync_Completes()
        {
            var svc = new TransferSpeedEventsHostedService(new Mock<ISpeedEventMigrationService>().Object, Options.Create(new MigrationCommandConfiguration()));
            await svc.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task UpgradeTo5HostedService_StopAsync_Completes()
        {
            var svc = new UpgradeTo5HostedService(NullLogger<UpgradeTo5HostedService>.Instance, new Mock<IConfigurationMigrationService>().Object, new Mock<IEventLogMigrationService>().Object, new Mock<ISpeedEventMigrationService>().Object, Options.Create(new UpgradeTo5CommandConfiguration()));
            await svc.StopAsync(CancellationToken.None);
        }
    }
}
