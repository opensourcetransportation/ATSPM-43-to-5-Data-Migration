#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator/Program.cs
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using Utah.Udot.Atspm.Infrastructure.Extensions;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var rootCommand = new DataMigratorCommands();
var commandBuilder = new CommandLineBuilder(rootCommand);
commandBuilder.UseDefaults();
commandBuilder.UseHost(
    hostBuilder => Host.CreateDefaultBuilder(hostBuilder)
        .ApplyVolumeConfiguration()
        .ConfigureAppConfiguration((_, config) =>
        {
            config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: false);
            config.AddUserSecrets<Program>(optional: true);
            config.AddCommandLine(args);
        })
        .ConfigureServices((hostContext, services) =>
        {
            services.AddAtspmDbContext(hostContext);
            services.AddAtspmEFConfigRepositories();
            services.AddAtspmEFEventLogRepositories();
            services.Configure<MigrationCommandConfiguration>(hostContext.Configuration.GetSection(nameof(MigrationCommandConfiguration)));
            services.Configure<TransferConfigCommandConfiguration>(hostContext.Configuration.GetSection(nameof(TransferConfigCommandConfiguration)));
            services.Configure<UpgradeTo5CommandConfiguration>(hostContext.Configuration.GetSection(nameof(UpgradeTo5CommandConfiguration)));
        }),
    host =>
    {
        var command = host.GetInvocationContext().ParseResult.CommandResult.Command;
        host.ConfigureServices((context, services) =>
        {
            if (command is ICommandOption commandOption)
            {
                commandOption.BindCommandOptions(context, services);
            }
        });
    });
var parser = commandBuilder.Build();
return await parser.InvokeAsync(args);
