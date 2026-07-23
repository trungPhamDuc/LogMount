namespace LogMount.Models;

public class ExpensivePart
{
    public int Id { get; set; }
    public string? PartsName { get; set; }
    public decimal? Cost { get; set; }
    public string? SourceFileName { get; set; }
    public DateTime? UploadedAt { get; set; }
}
