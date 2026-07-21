namespace LogMount.Models;

public class ExpensivePartFilterCriteria
{
    public string? PartsName { get; set; }
    public string? Line { get; set; }
    public string? Machine { get; set; }
    public string? ErrorName { get; set; }
    public string SortDirection { get; set; } = "desc";

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(PartsName) ||
        !string.IsNullOrWhiteSpace(Line) ||
        !string.IsNullOrWhiteSpace(Machine) ||
        !string.IsNullOrWhiteSpace(ErrorName);

    public bool IsDescending =>
        !SortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase);
}
