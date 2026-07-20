namespace LogMount.Models;

public class LogFilterCriteria
{
    public string? Date { get; set; }
    public string? Line { get; set; }
    public string? Language { get; set; }
    public string? OccurrenceTime { get; set; }
    public string? LotName { get; set; }
    public string? ErrorNo { get; set; }
    public string? ErrorName { get; set; }
    public string? Lane { get; set; }
    public string? Table { get; set; }
    public string? PartsNo { get; set; }
    public string? PartsName { get; set; }
    public string? HeadNo { get; set; }
    public string? NozzleType { get; set; }
    public string? FeederNo { get; set; }
    public string? FeederId { get; set; }
    public string? CartId { get; set; }
    public string? VisErrorNo { get; set; }
    public string? ErrorVacuum { get; set; }

    public bool HasAnyFilter =>
        !string.IsNullOrWhiteSpace(Date) ||
        !string.IsNullOrWhiteSpace(Line) ||
        !string.IsNullOrWhiteSpace(Language) ||
        !string.IsNullOrWhiteSpace(OccurrenceTime) ||
        !string.IsNullOrWhiteSpace(LotName) ||
        !string.IsNullOrWhiteSpace(ErrorNo) ||
        !string.IsNullOrWhiteSpace(ErrorName) ||
        !string.IsNullOrWhiteSpace(Lane) ||
        !string.IsNullOrWhiteSpace(Table) ||
        !string.IsNullOrWhiteSpace(PartsNo) ||
        !string.IsNullOrWhiteSpace(PartsName) ||
        !string.IsNullOrWhiteSpace(HeadNo) ||
        !string.IsNullOrWhiteSpace(NozzleType) ||
        !string.IsNullOrWhiteSpace(FeederNo) ||
        !string.IsNullOrWhiteSpace(FeederId) ||
        !string.IsNullOrWhiteSpace(CartId) ||
        !string.IsNullOrWhiteSpace(VisErrorNo) ||
        !string.IsNullOrWhiteSpace(ErrorVacuum);
}
