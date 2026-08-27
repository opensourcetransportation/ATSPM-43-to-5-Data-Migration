#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Commands/TransferSpeedEventsCommand.cs
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

public sealed class TransferSpeedEventsCommand : Command, ICommandOption<MigrationCommandConfiguration>
{
    public TransferSpeedEventsCommand() : base("transfer-speed", "Move ATSPM 4.3 speed events from SQL Server into the configured ATSPM 5 target")
    {
        AddOption(SourceOption);
        AddOption(StartOption);
        AddOption(EndOption);
        AddOption(LocationsOption);
    }

    public Option<string> SourceOption { get; } = new("--source", "Connection string for the ATSPM 4.3 SQL Server source") { IsRequired = true };
    public Option<DateTime> StartOption { get; } = new("--start", "Start date for the speed-event transfer") { IsRequired = true };
    public Option<DateTime> EndOption { get; } = new("--end", "Inclusive end; a date-only value includes the whole end day") { IsRequired = true };
    public Option<string> LocationsOption { get; } = new("--locations", "Comma-separated list of location identifiers") { IsRequired = false };

    public ModelBinder<MigrationCommandConfiguration> GetOptionsBinder()
    {
        var binder = new ModelBinder<MigrationCommandConfiguration>();
        binder.BindMemberFromValue(c => c.Source, SourceOption);
        binder.BindMemberFromValue(c => c.Start, StartOption);
        binder.BindMemberFromValue(c => c.End, EndOption);
        binder.BindMemberFromValue(c => c.Locations, LocationsOption);
        return binder;
    }

    public void BindCommandOptions(HostBuilderContext host, IServiceCollection services)
    {
        var rawEnd = host.GetInvocationContext().ParseResult.FindResultFor(EndOption)?.Tokens.LastOrDefault()?.Value
            ?? host.Configuration.GetSection(nameof(MigrationCommandConfiguration))[nameof(MigrationCommandConfiguration.End)];
        services.AddSingleton(GetOptionsBinder());
        services.AddOptions<MigrationCommandConfiguration>().Bind(host.Configuration.GetSection(nameof(MigrationCommandConfiguration)));
        services.AddOptions<MigrationCommandConfiguration>().BindCommandLine();
        services.PostConfigure<MigrationCommandConfiguration>(options =>
            options.EndIsDateOnly = MigrationDateRange.IsDateOnly(rawEnd));
        services.AddScoped<ISpeedEventMigrationService, SpeedEventMigrationService>();
        services.AddHostedService<TransferSpeedEventsHostedService>();
    }
}
