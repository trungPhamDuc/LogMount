namespace LogMount.Models;

public class DailyImprovementItem
{
    public int Rank { get; set; }
    public string Date { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double CumulativePercentage { get; set; }
    public string TopPartsOrErrors { get; set; } = string.Empty;
    public string TrendIndicator { get; set; } = string.Empty;
}
