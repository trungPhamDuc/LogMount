namespace LogMount.Models;

public class ExpensivePartSummaryItem
{
    public string PartsName { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string SideLabel { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string FeederNo { get; set; } = string.Empty;
    public string ErrorNo { get; set; } = string.Empty;
    public string ErrorName { get; set; } = string.Empty;
    public int Count { get; set; }
}
