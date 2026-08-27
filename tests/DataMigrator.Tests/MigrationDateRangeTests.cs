#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Tests/MigrationDateRangeTests.cs
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
using Xunit;

namespace DataMigrator.Tests;

public sealed class MigrationDateRangeTests
{
    [Fact]
    public void EnumerateHourlyWindows_SplitsPartialHoursCorrectly()
    {
        var windows = MigrationDateRange
            .EnumerateHourlyWindows(
                new DateTime(2024, 1, 1, 10, 15, 0),
                new DateTime(2024, 1, 1, 12, 30, 0))
            .ToList();

        Assert.Equal(3, windows.Count);
        Assert.Equal((new DateTime(2024, 1, 1, 10, 15, 0), new DateTime(2024, 1, 1, 11, 0, 0)), windows[0]);
        Assert.Equal((new DateTime(2024, 1, 1, 11, 0, 0), new DateTime(2024, 1, 1, 12, 0, 0)), windows[1]);
        Assert.Equal((new DateTime(2024, 1, 1, 12, 0, 0), new DateTime(2024, 1, 1, 12, 30, 0)), windows[2]);
    }

    [Fact]
    public void NormalizeInclusiveEndToExclusive_ExtendsDateOnlyRangeByOneDay()
    {
        var endExclusive = MigrationDateRange.NormalizeInclusiveEndToExclusive(
            new DateTime(2024, 1, 1, 0, 0, 0),
            new DateTime(2024, 1, 7, 0, 0, 0),
            endIsDateOnly: true);

        Assert.Equal(new DateTime(2024, 1, 8, 0, 0, 0), endExclusive);
    }

    [Fact]
    public void NormalizeInclusiveEndToExclusive_MakesTimedEventRangePrecise()
    {
        var endInclusive = new DateTime(2024, 1, 7, 23, 59, 59);

        var endExclusive = MigrationDateRange.NormalizeInclusiveEndToExclusive(
            new DateTime(2024, 1, 1, 0, 0, 0),
            endInclusive);

        Assert.Equal(endInclusive.AddTicks(1), endExclusive);
    }

    [Fact]
    public void NormalizeInclusiveEndToExclusive_PreservesFractionalPrecisionWhenPresent()
    {
        var endInclusive = new DateTime(2024, 1, 7, 23, 59, 59).AddMilliseconds(250);

        var endExclusive = MigrationDateRange.NormalizeInclusiveEndToExclusive(
            new DateTime(2024, 1, 1, 0, 0, 0),
            endInclusive);

        Assert.Equal(endInclusive.AddTicks(1), endExclusive);
    }

    [Fact]
    public void NormalizeInclusiveEndToExclusive_ExplicitMidnightAddsOnlyOneTick()
    {
        var endInclusive = new DateTime(2024, 1, 2, 0, 0, 0);

        var endExclusive = MigrationDateRange.NormalizeInclusiveEndToExclusive(
            new DateTime(2024, 1, 1, 0, 0, 0),
            endInclusive,
            endIsDateOnly: false);

        Assert.Equal(endInclusive.AddTicks(1), endExclusive);
    }

    [Theory]
    [InlineData("2024-01-02", true)]
    [InlineData("01/07/2024", true)]
    [InlineData("2024-01-02T00:00:00", false)]
    [InlineData("2024-01-02 00:00:00", false)]
    public void IsDateOnly_DistinguishesDateOnlyFromExplicitMidnight(string value, bool expected)
    {
        Assert.Equal(expected, MigrationDateRange.IsDateOnly(value));
    }
}
