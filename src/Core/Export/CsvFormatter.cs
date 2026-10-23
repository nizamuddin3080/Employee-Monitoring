using System.Text;

namespace LocalActivityTracker.Core.Export;

/// <summary>
/// Formatter implementing RFC-4180 escaping and Excel formula injection neutralization.
/// </summary>
public static class CsvFormatter
{
    private static readonly char[] FormulaTriggerChars = { '=', '+', '-', '@', '\t', '\r' };

    /// <summary>
    /// Neutralizes CSV formula injection by prepending a single quote (') if the field begins
    /// with =, +, -, @, tab, or carriage return, and wraps in double quotes with RFC-4180 quote escaping.
    /// </summary>
    public static string EscapeAndSanitize(string? input)
    {
        if (input == null)
        {
            return "\"\"";
        }

        if (input.Length == 0)
        {
            return "\"\"";
        }

        var text = input;

        // Neutralize Excel/Sheets formula execution
        if (FormulaTriggerChars.Contains(text[0]))
        {
            text = "'" + text;
        }

        // RFC-4180: double any embedded double quotes
        var escaped = text.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    /// <summary>
    /// Formats scalar values appropriately: non-negative integers as unquoted numbers, strings escaped with formula defense.
    /// </summary>
    public static string FormatValue(object? val)
    {
        if (val == null)
        {
            return "\"\"";
        }

        if (val is int or long or uint or ulong or short or ushort or byte)
        {
            return val.ToString()!;
        }

        return EscapeAndSanitize(val.ToString());
    }
}
