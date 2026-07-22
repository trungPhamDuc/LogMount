namespace LogMount.Models;

public class ExpensivePartTopItem
{
    public int Rank { get; set; }
    public string PartsName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal TotalCost { get; set; }
    public int TotalCount { get; set; }
    public int ErrorGroupCount { get; set; }
}
