namespace LogMount.Models;

public class RetryLogEntry
{
    public int Id { get; set; }
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
    public string? SourceFileName { get; set; }
    public string? UploadBatchId { get; set; }
    public DateTime? UploadedAt { get; set; }
}
