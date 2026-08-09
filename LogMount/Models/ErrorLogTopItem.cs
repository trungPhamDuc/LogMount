namespace LogMount.Models;

public class ErrorLogTopItem
{
    public int Rank { get; set; }
    public string Error { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int LocationGroupCount { get; set; }
}
