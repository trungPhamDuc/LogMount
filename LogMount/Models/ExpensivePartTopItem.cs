namespace LogMount.Models;

public class ExpensivePartTopItem
{
    public int Rank { get; set; }
    public string PartsName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int ErrorGroupCount { get; set; }
}
