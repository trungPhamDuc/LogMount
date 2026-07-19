namespace LogMount.Models;

public class RetryLogEntry
{
    public string Language { get; set; } = string.Empty;
    public string OccurrenceTime { get; set; } = string.Empty;
    public string LotName { get; set; } = string.Empty;
    public string ErrorNo { get; set; } = string.Empty;
    public string ErrorName { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string PartsNo { get; set; } = string.Empty;
    public string PartsName { get; set; } = string.Empty;
    public string HeadNo { get; set; } = string.Empty;
    public string NozzleType { get; set; } = string.Empty;
    public string FeederNo { get; set; } = string.Empty;
    public string FeederId { get; set; } = string.Empty;
    public string CartId { get; set; } = string.Empty;
    public string VisErrorNo { get; set; } = string.Empty;
    public string ErrorVacuum { get; set; } = string.Empty;
}
