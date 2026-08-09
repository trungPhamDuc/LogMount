namespace LogMount.Models;

public class ErrorLogFilterCriteria
{
    public string? Date { get; set; }
    public string? EventDate { get; set; }
    public string? Line { get; set; }
    public string? Lane { get; set; }
    public string? Table { get; set; }
    public string? Error { get; set; }
    public string? EventNo { get; set; }
    public string? ProgramName { get; set; }
    public string? Details { get; set; }
    public string? Side { get; set; }
    public string? Machine { get; set; }
    public string SortDirection { get; set; } = "location";

    public bool IsCountSort =>
        SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase) ||
        SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase);

    public bool IsDescending =>
        !SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase);

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Date) ||
        !string.IsNullOrWhiteSpace(EventDate) ||
        !string.IsNullOrWhiteSpace(Line) ||
        !string.IsNullOrWhiteSpace(Lane) ||
        !string.IsNullOrWhiteSpace(Table) ||
        !string.IsNullOrWhiteSpace(Error) ||
        !string.IsNullOrWhiteSpace(EventNo) ||
        !string.IsNullOrWhiteSpace(ProgramName) ||
        !string.IsNullOrWhiteSpace(Details) ||
        !string.IsNullOrWhiteSpace(Side) ||
        !string.IsNullOrWhiteSpace(Machine);

    public bool HasSummaryFilter =>
        HasAnyFilter ||
        !SortDirection.Equals("location", StringComparison.OrdinalIgnoreCase);
}
