namespace LogMount.Models;

public class ExpensivePartFilterCriteria
{
    public string? PartsName { get; set; }
    public string? Line { get; set; }
    public string? Lane { get; set; }
    public string? Machine { get; set; }
    public string? Shift { get; set; }
    public string? ErrorName { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string SortDirection { get; set; } = "location";

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(PartsName) ||
        !string.IsNullOrWhiteSpace(Line) ||
        !string.IsNullOrWhiteSpace(Lane) ||
        !string.IsNullOrWhiteSpace(Machine) ||
        !string.IsNullOrWhiteSpace(Shift) ||
        !string.IsNullOrWhiteSpace(ErrorName) ||
        !string.IsNullOrWhiteSpace(DateFrom) ||
        !string.IsNullOrWhiteSpace(DateTo);

    public bool IsCountSort =>
        SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase) ||
        SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase);

    public bool IsCostSort =>
        SortDirection.Equals("cost-desc", StringComparison.OrdinalIgnoreCase) ||
        SortDirection.Equals("cost-asc", StringComparison.OrdinalIgnoreCase);

    public bool IsCostDescending =>
        !SortDirection.Equals("cost-asc", StringComparison.OrdinalIgnoreCase);

    public bool IsDescending =>
        !SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase);
}
