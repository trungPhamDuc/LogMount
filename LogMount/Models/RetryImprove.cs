using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LogMount.Models;

[Table("RetryImprove")]
public class RetryImprove
{
    [Key]
    public int Id { get; set; }

    [MaxLength(255)]
    public string? PartsName { get; set; }

    [MaxLength(50)]
    public string? Line { get; set; }

    [MaxLength(50)]
    public string? Lane { get; set; }

    [MaxLength(50)]
    public string? Side { get; set; }

    [MaxLength(50)]
    public string? Machine { get; set; }

    [MaxLength(50)]
    public string? Feeder { get; set; }

    [Required]
    [MaxLength(255)]
    public string EngineerName { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string ActionTaken { get; set; } = string.Empty;

    public DateTime ExecutionDate { get; set; } = DateTime.Today;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
