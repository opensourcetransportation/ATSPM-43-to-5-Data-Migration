#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/MigrationLocationSelection.cs
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

using Utah.Udot.Atspm.Data.Enums;
using Utah.Udot.Atspm.Data.Models;

namespace DataMigrator.Services;

public static class MigrationLocationSelection
{
    public static List<Location> FilterLocations(
        IEnumerable<Location> locations,
        DeviceTypes? requiredDeviceType,
        string? locationIdentifiers)
    {
        var filteredLocations = locations.ToList();

        if (requiredDeviceType.HasValue)
        {
            filteredLocations = filteredLocations
                .Where(location => location.Devices.Any(device => device.DeviceType == requiredDeviceType.Value))
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(locationIdentifiers))
        {
            return filteredLocations;
        }

        var allowedIdentifiers = locationIdentifiers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filteredLocations
            .Where(location => allowedIdentifiers.Contains(location.LocationIdentifier))
            .ToList();
    }
}
