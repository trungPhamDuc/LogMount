namespace LogMount.Models;

public class ErrorLogEntry
{
    public int Id { get; set; }
    public string? Date { get; set; }
    public string? EventDate { get; set; }
    public string? Line { get; set; }
    public string? Lane { get; set; }
    public string? Table { get; set; }
    public string? Error { get; set; }
    public string? EventNo { get; set; }
    public string? ProgramName { get; set; }
    public string? Details { get; set; }
    public string? SourceFileName { get; set; }
    public string? UploadBatchId { get; set; }
    public DateTime? UploadedAt { get; set; }
}
