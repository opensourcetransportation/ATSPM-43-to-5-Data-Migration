#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Commands/TransferConfigCommand.cs
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.CommandLine.Hosting;
using System.CommandLine.NamingConventionBinder;

namespace DataMigrator.Commands;

public sealed class TransferConfigCommand : Command, ICommandOption<TransferConfigCommandConfiguration>
{
    public TransferConfigCommand() : base("transfer-config", "Move ATSPM 4.3 configuration data from SQL Server into the configured ATSPM 5 target")
    {
        AddOption(SourceOption);
        AddOption(DeleteOption);
        AddOption(UpdateLocationsOption);
        AddOption(ImportSpeedDevicesOption);
    }

    public Option<string> SourceOption { get; } = new("--source", "Connection string for the ATSPM 4.3 SQL Server source") { IsRequired = true };
    public Option<bool> DeleteOption { get; } = new("--delete", "Delete target configuration data before importing");
    public Option<bool> UpdateLocationsOption { get; } = new("--update-locations", () => true, "Import configuration and location data into the target (enabled by default)");
    public Option<bool> ImportSpeedDevicesOption { get; } = new("--update-speed", () => true, "Import speed-device configuration, not speed events (enabled by default)");

    public ModelBinder<TransferConfigCommandConfiguration> GetOptionsBinder()
    {
        var binder = new ModelBinder<TransferConfigCommandConfiguration>();
        binder.BindMemberFromValue(c => c.Source, SourceOption);
        binder.BindMemberFromValue(c => c.Delete, DeleteOption);
        binder.BindMemberFromValue(c => c.UpdateLocations, UpdateLocationsOption);
        binder.BindMemberFromValue(c => c.ImportSpeedDevices, ImportSpeedDevicesOption);
        return binder;
    }

    public void BindCommandOptions(HostBuilderContext host, IServiceCollection services)
    {
        services.AddSingleton(GetOptionsBinder());
        services.AddOptions<TransferConfigCommandConfiguration>().Bind(host.Configuration.GetSection(nameof(TransferConfigCommandConfiguration)));
        services.AddOptions<TransferConfigCommandConfiguration>().BindCommandLine();
        services.AddScoped<IConfigurationMigrationService, ConfigurationMigrationService>();
        services.AddHostedService<TransferConfigCommandHostedService>();
    }
}

public sealed class TransferConfigCommandConfiguration
{
    public string Source { get; set; } = string.Empty;
    public bool Delete { get; set; }
    public bool UpdateLocations { get; set; } = true;
    public bool ImportSpeedDevices { get; set; } = true;
}
