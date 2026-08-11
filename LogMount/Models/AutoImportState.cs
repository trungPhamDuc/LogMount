namespace LogMount.Models;

public class AutoImportState
{
    public string JobName { get; set; } = string.Empty;
    public string? LastRunDate { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastCompletedAt { get; set; }
}
