namespace LogMount.Services;

public static class LotNameParser
{
    /// <summary>
    /// Parses Lot Name like "EBR26597304_B_A_L12_V10_LANE2_24CY" into "L1-B-A".
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

        return $"L{lineNo}-{side}-{machine}";
    }

    public static string ParseLineNumber(string? lotName)
    {
        if (TryParseLotNameParts(lotName, out _, out _, out var lineSegment))
        {
            if (lineSegment.Length > 1 && (lineSegment[0] == 'L' || lineSegment[0] == 'l'))
            {
                var digits = lineSegment[1..];
                if (digits.Length > 0)
                {
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
                return (segments[0], segments[1], segments[2]);
            }
        }

        return (ParseLineNumber(lotName), ParseSide(lotName), ParseMachine(lotName));
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

    public static string ExtractDate(string? occurrenceTime)
    {
        if (string.IsNullOrWhiteSpace(occurrenceTime))
        {
            return string.Empty;
        }

        var spaceIndex = occurrenceTime.IndexOf(' ');
        return spaceIndex > 0
            ? occurrenceTime[..spaceIndex].Trim()
            : occurrenceTime.Trim();
    }
}
