namespace LogMount.Models;

public class RequestLogEntry
{
    public int Id { get; set; }
    public string? Date { get; set; }              // 1. DATE (Ngày/tháng)
    public string? Shift { get; set; }             // 2. SHIFT
    public string? Line { get; set; }              // 3. LINE
    public string? Process { get; set; }           // 4. PROCESS
    public string? ModelSuffix { get; set; }       // 5. MODEL.SUFFIX
    public string? Chassis { get; set; }           // 6. CHASSIS
    public string? Board { get; set; }             // 7. BOARD
    public string? PartAssy { get; set; }          // 8. PART Ass'y (Mã cụm)
    public string? WorkOrder { get; set; }         // 9. W/O (Đơn hàng)
    public string? PartNo { get; set; }            // 10. P/N (Mã hàng)
    public string? PidOrLot { get; set; }          // 11. PID or Lot (Số tem - Lô sx)
    public string? Unit { get; set; }              // 12. Unit (Đ.V)
    public decimal PQty { get; set; }              // 13. P.Qty (S.lg SX)
    public decimal RQty { get; set; }              // 14. R.Q'ty (Slg YC - lk bị rơi)
    public decimal AmtOnRequest { get; set; }      // 15. Amt on request (VND)
    public string? Remarks { get; set; }           // 16. Remarks (Ghi chú)
    public string? Department { get; set; }        // 17. BO PHAN (Bộ phận)
    public string? StatusRemarks { get; set; }     // 18. Remarks (Trạng thái)

    public string? SourceFileName { get; set; }
    public string? UploadBatchId { get; set; }
    public DateTime? UploadedAt { get; set; }
}
