namespace LogMount.Models;

public class ErrorLogSummaryItem
{
    public string Error { get; set; } = string.Empty;
    public string EventNo { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Dates { get; set; } = string.Empty;
    public string Lines { get; set; } = string.Empty;
    public string Lanes { get; set; } = string.Empty;
    public string Tables { get; set; } = string.Empty;
}
