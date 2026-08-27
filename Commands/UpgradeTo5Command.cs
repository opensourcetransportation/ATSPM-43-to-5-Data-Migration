#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Commands/UpgradeTo5Command.cs
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

public sealed class UpgradeTo5Command : Command, ICommandOption<UpgradeTo5CommandConfiguration>
{
    public UpgradeTo5Command() : base("upgrade-to-5", "Run the standard ATSPM 4.3 to ATSPM 5 migration sequence")
    {
        AddOption(SourceOption);
        AddOption(StartOption);
        AddOption(EndOption);
        AddOption(DeleteOption);
        AddOption(UpdateLocationsOption);
        AddOption(ImportSpeedDevicesOption);
        AddOption(BatchOption);
        AddOption(DeviceOption);
        AddOption(LocationsOption);
        AddOption(SkipConfigOption);
        AddOption(SkipEventsOption);
        AddOption(SkipSpeedOption);
    }

    public Option<string> SourceOption { get; } = new("--source", "Connection string for the ATSPM 4.3 SQL Server source; may also come from UpgradeTo5CommandConfiguration");
    public Option<DateTime> StartOption { get; } = new("--start", "Start date/time for event and speed migration; may also come from UpgradeTo5CommandConfiguration");
    public Option<DateTime> EndOption { get; } = new("--end", "Inclusive end; a date-only value includes the whole end day; may also come from UpgradeTo5CommandConfiguration");
    public Option<bool> DeleteOption { get; } = new("--delete", "Delete target configuration data before importing");
    public Option<bool> UpdateLocationsOption { get; } = new("--update-locations", () => true, "Import configuration and location data into the target (enabled by default)");
    public Option<bool> ImportSpeedDevicesOption { get; } = new("--update-speed", () => true, "Import speed-device configuration, not speed events (enabled by default)");
    public Option<int?> BatchOption { get; } = new("--batch", "Compressed-window insert batch size (maximum 600)") { IsRequired = false };
    public Option<int?> DeviceOption { get; } = new("--device", "Limit event location selection to one ATSPM DeviceTypes integer; normally omit") { IsRequired = false };
    public Option<string> LocationsOption { get; } = new("--locations", "Comma-separated list of location identifiers") { IsRequired = false };
    public Option<bool> SkipConfigOption { get; } = new("--skip-config", "Skip configuration migration");
    public Option<bool> SkipEventsOption { get; } = new("--skip-events", "Skip event log migration");
    public Option<bool> SkipSpeedOption { get; } = new("--skip-speed", "Skip speed-event migration");

    public ModelBinder<UpgradeTo5CommandConfiguration> GetOptionsBinder()
    {
        var binder = new ModelBinder<UpgradeTo5CommandConfiguration>();
        binder.BindMemberFromValue(c => c.Source, SourceOption);
        binder.BindMemberFromValue(c => c.Start, StartOption);
        binder.BindMemberFromValue(c => c.End, EndOption);
        binder.BindMemberFromValue(c => c.Delete, DeleteOption);
        binder.BindMemberFromValue(c => c.UpdateLocations, UpdateLocationsOption);
        binder.BindMemberFromValue(c => c.ImportSpeedDevices, ImportSpeedDevicesOption);
        binder.BindMemberFromValue(c => c.Batch, BatchOption);
        binder.BindMemberFromValue(c => c.Device, DeviceOption);
        binder.BindMemberFromValue(c => c.Locations, LocationsOption);
        binder.BindMemberFromValue(c => c.SkipConfig, SkipConfigOption);
        binder.BindMemberFromValue(c => c.SkipEvents, SkipEventsOption);
        binder.BindMemberFromValue(c => c.SkipSpeed, SkipSpeedOption);
        return binder;
    }

    public void BindCommandOptions(HostBuilderContext host, IServiceCollection services)
    {
        var rawEnd = host.GetInvocationContext().ParseResult.FindResultFor(EndOption)?.Tokens.LastOrDefault()?.Value
            ?? host.Configuration.GetSection(nameof(UpgradeTo5CommandConfiguration))[nameof(UpgradeTo5CommandConfiguration.End)];
        services.AddSingleton(GetOptionsBinder());
        services.AddOptions<UpgradeTo5CommandConfiguration>().Bind(host.Configuration.GetSection(nameof(UpgradeTo5CommandConfiguration)));
        services.AddOptions<UpgradeTo5CommandConfiguration>().BindCommandLine();
        services.PostConfigure<UpgradeTo5CommandConfiguration>(options =>
            options.EndIsDateOnly = MigrationDateRange.IsDateOnly(rawEnd));
        services.AddScoped<IConfigurationMigrationService, ConfigurationMigrationService>();
        services.AddScoped<IEventLogMigrationService, EventLogMigrationService>();
        services.AddScoped<ISpeedEventMigrationService, SpeedEventMigrationService>();
        services.AddHostedService<UpgradeTo5HostedService>();
    }
}

public sealed class UpgradeTo5CommandConfiguration
{
    public string Source { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool EndIsDateOnly { get; set; }
    public bool Delete { get; set; }
    public bool UpdateLocations { get; set; } = true;
    public bool ImportSpeedDevices { get; set; } = true;
    public int? Batch { get; set; }
    public int? Device { get; set; }
    public string? Locations { get; set; }
    public bool SkipConfig { get; set; }
    public bool SkipEvents { get; set; }
    public bool SkipSpeed { get; set; }
}
