namespace LogMount.Models;

public class ErrorSummaryItem
{
    public string ErrorNo { get; set; } = string.Empty;
    public string ErrorName { get; set; } = string.Empty;
    public int Count { get; set; }
    public string OccurrenceTimes { get; set; } = string.Empty;
    public string LotNames { get; set; } = string.Empty;
    public string Lanes { get; set; } = string.Empty;
    public string Tables { get; set; } = string.Empty;
    public string PartsNumbers { get; set; } = string.Empty;
    public string PartsNames { get; set; } = string.Empty;
    public string HeadNumbers { get; set; } = string.Empty;
    public string NozzleTypes { get; set; } = string.Empty;
    public string FeederNumbers { get; set; } = string.Empty;
    public string FeederIds { get; set; } = string.Empty;
    public string CartIds { get; set; } = string.Empty;
    public string VisErrorNumbers { get; set; } = string.Empty;
    public string ErrorVacuums { get; set; } = string.Empty;
}
