namespace LogMount.Models;

public class RetryLogImprovementCriteria
{
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? Line { get; set; }
    public string? Lane { get; set; }
    public string? Side { get; set; }
    public string? Machine { get; set; }
    public string? Table { get; set; }
    public string? PartsName { get; set; }
    public string? ErrorName { get; set; }
    public string? ErrorNo { get; set; }
    public int TopN { get; set; } = 20;

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(FromDate) ||
        !string.IsNullOrWhiteSpace(ToDate) ||
        !string.IsNullOrWhiteSpace(Line) ||
        !string.IsNullOrWhiteSpace(Lane) ||
        !string.IsNullOrWhiteSpace(Side) ||
        !string.IsNullOrWhiteSpace(Machine) ||
        !string.IsNullOrWhiteSpace(Table) ||
        !string.IsNullOrWhiteSpace(PartsName) ||
        !string.IsNullOrWhiteSpace(ErrorName) ||
        !string.IsNullOrWhiteSpace(ErrorNo);
}
