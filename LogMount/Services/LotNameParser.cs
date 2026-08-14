namespace LogMount.Services;

public static class LotNameParser
{
    /// <summary>
    /// Extracts date part from occurrenceTime string like "2026/08/13 10:37:30" => "2026/08/13".
    /// </summary>
    public static string ExtractDate(string? occurrenceTime)
    {
        if (string.IsNullOrWhiteSpace(occurrenceTime))
        {
            return string.Empty;
        }

        var trimmed = occurrenceTime.Trim();
        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return trimmed[..spaceIndex];
        }

        return trimmed;
    }

    /// <summary>
    /// Parses Lot Name like "EBR26597304_B_A_L12_V10_LANE2_24CY" into "L1-B-A" or "LLTE-B-A".
    /// </summary>
    public static string ParseLine(string? lotName)
    {
        if (string.IsNullOrWhiteSpace(lotName))
        {
            return string.Empty;
        }

        var parts = lotName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return string.Empty;
        }

        var side = parts[1].ToUpperInvariant();
        var machine = parts[2].ToUpperInvariant();
        var lineSegment = parts[3];

        var lineNo = "0";
        if (lineSegment.Length > 1 && (lineSegment[0] == 'L' || lineSegment[0] == 'l'))
        {
            var digits = lineSegment[1..];
            if (digits.Length > 0)
            {
                lineNo = digits[0].ToString();
            }
        }

        var displayLine = (lineNo == "0" || lineSegment.Contains("LTE", StringComparison.OrdinalIgnoreCase))
            ? "LLTE"
            : $"L{lineNo}";

        return $"{displayLine}-{side}-{machine}";
    }

    public static string ParseLineNumber(string? lotName)
    {
        if (TryParseLotNameParts(lotName, out _, out _, out var lineSegment))
        {
            if (lineSegment.Contains("LTE", StringComparison.OrdinalIgnoreCase))
            {
                return "LLTE";
            }

            if (lineSegment.Length > 1 && (lineSegment[0] == 'L' || lineSegment[0] == 'l'))
            {
                var digits = lineSegment[1..];
                if (digits.Length > 0)
                {
                    if (digits[0] == '0')
                    {
                        return "LLTE";
                    }
                    return $"L{digits[0]}";
                }
            }
        }

        return string.Empty;
    }

    public static string ParseSide(string? lotName)
    {
        return TryParseLotNameParts(lotName, out var side, out _, out _)
            ? side
            : string.Empty;
    }

    public static string ParseMachine(string? lotName)
    {
        return TryParseLotNameParts(lotName, out _, out var machine, out _)
            ? machine
            : string.Empty;
    }

    public static string GetSideLabel(string? side) =>
        (side ?? string.Empty).ToUpperInvariant() switch
        {
            "B" => "Bot",
            "T" => "Top",
            _ => side ?? string.Empty
        };

    public static (string LineNumber, string Side, string Machine) ParseLineComponents(string? lineValue, string? lotName)
    {
        if (!string.IsNullOrWhiteSpace(lineValue))
        {
            var segments = lineValue.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3)
            {
                var line = segments[0];
                if (string.Equals(line, "L0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(line, "Line 0", StringComparison.OrdinalIgnoreCase))
                {
                    line = "LLTE";
                }
                return (line, segments[1], segments[2]);
            }
        }

        var parsedLine = ParseLineNumber(lotName);
        if (string.Equals(parsedLine, "L0", StringComparison.OrdinalIgnoreCase))
        {
            parsedLine = "LLTE";
        }

        return (parsedLine, ParseSide(lotName), ParseMachine(lotName));
    }

    private static bool TryParseLotNameParts(
        string? lotName,
        out string side,
        out string machine,
        out string lineSegment)
    {
        side = string.Empty;
        machine = string.Empty;
        lineSegment = string.Empty;

        if (string.IsNullOrWhiteSpace(lotName))
        {
            return false;
        }

        var parts = lotName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return false;
        }

        side = parts[1].ToUpperInvariant();
        machine = parts[2].ToUpperInvariant();
        lineSegment = parts[3];

        return true;
    }
}
