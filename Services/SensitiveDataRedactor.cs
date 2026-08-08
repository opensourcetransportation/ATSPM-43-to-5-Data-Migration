using System.Text.RegularExpressions;

namespace DataMigrator.Services;

internal static partial class SensitiveDataRedactor
{
    [GeneratedRegex("""(?i)(Password|Pwd)\s*=\s*(?:"[^"]*"|'[^']*'|[^;\s]*)""")]
    private static partial Regex PasswordPattern();

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PasswordPattern().Replace(value, "$1=***");
    }
}
