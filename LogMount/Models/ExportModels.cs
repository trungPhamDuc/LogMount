namespace LogMount.Models;

public enum ExportFormat
{
    Csv,
    Xlsx
}

public enum ExportSection
{
    Overview,
    Errors,
    Logs
}

public class FileExportResult
{
    public required byte[] Content { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}
