#region license
// Copyright 2026 Utah Departement of Transportation
// for DataMigrator - DataMigrator.Services/MigrationDateRange.cs
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

namespace DataMigrator.Services;

public static class MigrationDateRange
{
    public static IEnumerable<(DateTime Start, DateTime End)> EnumerateHourlyWindows(DateTime startInclusive, DateTime endExclusive)
    {
        if (endExclusive <= startInclusive)
        {
            yield break;
        }

        var windowStart = startInclusive;
        while (windowStart < endExclusive)
        {
            var nextHourBoundary = new DateTime(
                windowStart.Year,
                windowStart.Month,
                windowStart.Day,
                windowStart.Hour,
                0,
                0,
                windowStart.Kind).AddHours(1);
            var windowEnd = nextHourBoundary < endExclusive ? nextHourBoundary : endExclusive;

            yield return (windowStart, windowEnd);
            windowStart = windowEnd;
        }
    }

    public static DateTime NormalizeInclusiveEndToExclusive(
        DateTime startInclusive,
        DateTime endInclusive,
        bool endIsDateOnly = false)
    {
        if (endIsDateOnly)
        {
            return endInclusive.Date.AddDays(1);
        }

        return endInclusive.AddTicks(1);
    }

    public static bool IsDateOnly(string? value) =>
        IsDateOnly(value, System.Globalization.CultureInfo.CurrentCulture) ||
        IsDateOnly(value, System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsDateOnly(string? value, System.Globalization.CultureInfo culture)
    {
        var formats = culture.DateTimeFormat.GetAllDateTimePatterns('d')
            .Concat(culture.DateTimeFormat.GetAllDateTimePatterns('D'))
            .Append("yyyy-MM-dd")
            .Distinct()
            .ToArray();

        return DateOnly.TryParseExact(
            value,
            formats,
            culture,
            System.Globalization.DateTimeStyles.AllowWhiteSpaces,
            out _);
    }
}
