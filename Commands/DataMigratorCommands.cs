#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Commands/DataMigratorCommands.cs
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

using System.CommandLine;

namespace DataMigrator.Commands;

public sealed class DataMigratorCommands : RootCommand
{
    public DataMigratorCommands() : base("DataMigrator utility for ATSPM 4.3 to 5.2 upgrades")
    {
        AddCommand(TransferConfigCommand);
        AddCommand(TransferEventsCommand);
        AddCommand(TransferSpeedCommand);
        AddCommand(UpgradeTo52Command);
    }

    public TransferConfigCommand TransferConfigCommand { get; } = new();
    public TransferEventsCommand TransferEventsCommand { get; } = new();
    public TransferSpeedEventsCommand TransferSpeedCommand { get; } = new();
    public UpgradeTo52Command UpgradeTo52Command { get; } = new();
}
