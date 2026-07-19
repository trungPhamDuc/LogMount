namespace LogMount.Models;

public class ColumnSummaryItem
{
    public string ColumnName { get; set; } = string.Empty;
    public string Values { get; set; } = string.Empty;
    public int DistinctCount { get; set; }
}
