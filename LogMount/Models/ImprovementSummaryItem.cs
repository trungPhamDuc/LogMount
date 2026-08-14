namespace LogMount.Models;

public class ImprovementSummaryItem
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double CumulativePercentage { get; set; }
    public string Lines { get; set; } = string.Empty;
    public string Lanes { get; set; } = string.Empty;
    public string Sides { get; set; } = string.Empty;
    public string Machines { get; set; } = string.Empty;
    public string Tables { get; set; } = string.Empty;
    public string ErrorInfo { get; set; } = string.Empty;
}
