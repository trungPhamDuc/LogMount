namespace LogMount.Models;

public class ErrorLogImprovementCriteria
{
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? Error { get; set; }
    public string? Line { get; set; }
    public string? Lane { get; set; }
    public string? Side { get; set; }
    public string? Machine { get; set; }
    public string? Table { get; set; }
    public int TopN { get; set; } = 20;

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(FromDate) ||
        !string.IsNullOrWhiteSpace(ToDate) ||
        !string.IsNullOrWhiteSpace(Error) ||
        !string.IsNullOrWhiteSpace(Line) ||
        !string.IsNullOrWhiteSpace(Lane) ||
        !string.IsNullOrWhiteSpace(Side) ||
        !string.IsNullOrWhiteSpace(Machine) ||
        !string.IsNullOrWhiteSpace(Table);
}
