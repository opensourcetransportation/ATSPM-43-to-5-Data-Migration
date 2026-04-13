#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Tests/CommandTests.cs
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
using System.CommandLine.Binding;
using Xunit;

namespace DataMigrator.Tests;

public sealed class CommandTests
{
    [Fact]
    public void RootCommand_ExposesExpectedCommands()
    {
        var command = new DataMigratorCommands();

        var names = command.Subcommands.Select(subcommand => subcommand.Name).OrderBy(name => name).ToArray();

        Assert.Equal(
            ["transfer-config", "transfer-events", "transfer-speed", "upgrade-to-5-2"],
            names);
    }

    [Fact]
    public void TransferConfigCommand_RequiresSource()
    {
        var command = new TransferConfigCommand();

        Assert.True(command.SourceOption.IsRequired);
        Assert.False(command.DeleteOption.IsRequired);
        Assert.False(command.UpdateLocationsOption.IsRequired);
        Assert.False(command.ImportSpeedDevicesOption.IsRequired);
    }

    [Fact]
    public void TransferEventsCommand_RequiresSourceStartAndEnd()
    {
        var command = new TransferEventsCommand();

        Assert.True(command.SourceOption.IsRequired);
        Assert.True(command.StartOption.IsRequired);
        Assert.True(command.EndOption.IsRequired);
        Assert.False(command.BatchOption.IsRequired);
        Assert.False(command.DeviceOption.IsRequired);
        Assert.False(command.LocationsOption.IsRequired);
    }

    [Fact]
    public void UpgradeCommand_RequiresSourceStartAndEnd()
    {
        var command = new UpgradeTo52Command();

        Assert.True(command.SourceOption.IsRequired);
        Assert.True(command.StartOption.IsRequired);
        Assert.True(command.EndOption.IsRequired);
        Assert.False(command.SkipConfigOption.IsRequired);
        Assert.False(command.SkipEventsOption.IsRequired);
        Assert.False(command.SkipSpeedOption.IsRequired);
    }

    [Fact]
    public void UpgradeCommand_BindsSkipFlagsFromCommandLine()
    {
        var command = new UpgradeTo52Command();
        var parser = new System.CommandLine.Parsing.Parser(command);
        var parseResult = parser.Parse(["--source", "source", "--start", "2024-01-01", "--end", "2024-01-02", "--skip-config", "--skip-events", "--skip-speed"], "");
        var invocationContext = new System.CommandLine.Invocation.InvocationContext(parseResult, new System.CommandLine.IO.TestConsole());

        var options = (UpgradeTo52CommandConfiguration)command.GetOptionsBinder().CreateInstance(invocationContext.BindingContext)!;

        Assert.True(options.SkipConfig);
        Assert.True(options.SkipEvents);
        Assert.True(options.SkipSpeed);
    }
}


