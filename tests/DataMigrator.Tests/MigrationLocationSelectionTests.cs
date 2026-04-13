#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Tests/MigrationLocationSelectionTests.cs
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
using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;
using Xunit;

namespace DataMigrator.Tests;

public sealed class MigrationLocationSelectionTests
{
    [Fact]
    public void FilterLocations_ReturnsAllLocations_WhenNoFiltersAreProvided()
    {
        var locations = CreateLocations();

        var filtered = MigrationLocationSelection.FilterLocations(locations, null, null);

        Assert.Equal(["1001", "1002", "ABC3"], filtered.Select(location => location.LocationIdentifier));
    }

    [Fact]
    public void FilterLocations_FiltersByDeviceType()
    {
        var locations = CreateLocations();

        var filtered = MigrationLocationSelection.FilterLocations(locations, DeviceTypes.SignalController, null);

        Assert.Equal(["1001", "ABC3"], filtered.Select(location => location.LocationIdentifier));
    }

    [Fact]
    public void FilterLocations_FiltersByLocationIdentifiers_CaseInsensitively()
    {
        var locations = CreateLocations();

        var filtered = MigrationLocationSelection.FilterLocations(locations, null, "1002,abc3");

        Assert.Equal(["1002", "ABC3"], filtered.Select(location => location.LocationIdentifier));
    }

    [Fact]
    public void FilterLocations_AppliesBothFiltersTogether()
    {
        var locations = CreateLocations();

        var filtered = MigrationLocationSelection.FilterLocations(locations, DeviceTypes.SpeedSensor, "1001,1002");

        Assert.Equal(["1002"], filtered.Select(location => location.LocationIdentifier));
    }

    private static List<Location> CreateLocations()
    {
        return
        [
            CreateLocation("1001", DeviceTypes.SignalController),
            CreateLocation("1002", DeviceTypes.SpeedSensor),
            CreateLocation("ABC3", DeviceTypes.SignalController, DeviceTypes.SpeedSensor)
        ];
    }

    private static Location CreateLocation(string identifier, params DeviceTypes[] deviceTypes)
    {
        return new Location
        {
            LocationIdentifier = identifier,
            Devices = deviceTypes
                .Select((deviceType, index) => new Device
                {
                    Id = index + 1,
                    DeviceType = deviceType
                })
                .ToList()
        };
    }
}
