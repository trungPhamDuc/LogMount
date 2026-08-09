namespace LogMount.Models;

public class ErrorLogDetailSummaryItem
{
    public string Error { get; set; } = string.Empty;
    public string EventNo { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string SideLabel { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public int Count { get; set; }
}
