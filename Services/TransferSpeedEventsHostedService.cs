#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/TransferSpeedEventsHostedService.cs
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DataMigrator.Services;

public sealed class TransferSpeedEventsHostedService : IHostedService
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ISpeedEventMigrationService? _service;
    private readonly MigrationCommandConfiguration _options;

    public TransferSpeedEventsHostedService(IServiceScopeFactory scopeFactory, IOptions<MigrationCommandConfiguration> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    internal TransferSpeedEventsHostedService(ISpeedEventMigrationService service, IOptions<MigrationCommandConfiguration> options)
    {
        _service = service;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_service != null)
        {
            await _service.RunAsync(_options, cancellationToken);
            return;
        }

        using var scope = _scopeFactory!.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISpeedEventMigrationService>().RunAsync(_options, cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
