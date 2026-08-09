using System.Text.RegularExpressions;

namespace LogMount.Services;

public static class ErrorLogHelper
{
    private static readonly Regex LineNumPattern = new(@"_L(?<line>\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LineWordPattern = new(@"LINE\s*(?<line>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? GetEffectiveLine(string? line, string? programName)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        if (string.IsNullOrWhiteSpace(programName))
        {
            return null;
        }

        var match = LineNumPattern.Match(programName);
        if (match.Success)
        {
            return $"Line {match.Groups["line"].Value}";
        }

        if (programName.Contains("LTE", StringComparison.OrdinalIgnoreCase))
        {
            return "Line LTE";
        }

        var lineWordMatch = LineWordPattern.Match(programName);
        if (lineWordMatch.Success)
        {
            return $"Line {lineWordMatch.Groups["line"].Value}";
        }

        return null;
    }

    public static string ParseSide(string? programName) => LotNameParser.ParseSide(programName);

    public static string ParseMachine(string? programName) => LotNameParser.ParseMachine(programName);

    public static string GetSideLabel(string? side) => LotNameParser.GetSideLabel(side);

    public static string GetErrorNameCssClass(string? errorName)
    {
        if (string.IsNullOrWhiteSpace(errorName))
        {
            return "error-name-tag error-name-empty";
        }

        var normalized = errorName.Trim().ToUpperInvariant();

        if (normalized.Contains("PICK UP") || normalized.Contains("PICKUP"))
        {
            return "error-name-tag error-name-pickup";
        }

        if (normalized.Contains("VACUUM") || normalized.Contains("VACCUM"))
        {
            return "error-name-tag error-name-vacuum";
        }

        if (normalized.Contains("NOZZLE"))
        {
            return "error-name-tag error-name-nozzle";
        }

        if (normalized.Contains("AUTORUN") || normalized.Contains("AUTO RUN"))
        {
            return "error-name-tag error-name-autorun";
        }

        if (normalized.Contains("PARTS"))
        {
            return "error-name-tag error-name-parts";
        }

        if (normalized.Contains("VISION") || normalized.Contains("CAMERA") || normalized.Contains("SIDE VIEW") || normalized.Contains("SIDEVIEW") || normalized.Contains("RECOGNITION"))
        {
            return "error-name-tag error-name-vision";
        }

        if (normalized.Contains("LEAD") || normalized.Contains("WIDTH"))
        {
            return "error-name-tag error-name-lead";
        }

        if (normalized.Contains("INTERLOCK") || normalized.Contains("NON-SAVED") || normalized.Contains("RESET"))
        {
            return "error-name-tag error-name-interlock";
        }

        if (normalized.Contains("SHAFT") || normalized.Contains("CLEANING") || normalized.Contains("BLOW"))
        {
            return "error-name-tag error-name-maintenance";
        }

        if (normalized.Contains("PALLET") || normalized.Contains("PRECEDE"))
        {
            return "error-name-tag error-name-pallet";
        }

        if (normalized.Contains("HALFWAY") || normalized.Contains("CONTINUE"))
        {
            return "error-name-tag error-name-control";
        }

        return "error-name-tag error-name-default";
    }
}
