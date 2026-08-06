using LogMount.Data;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LogMount.Pages;

public class ErrorLogIndexModel : PageModel
{
    private readonly IErrorLogParserService _parserService;
    private readonly IErrorLogImportService _importService;
    private readonly IErrorLogBatchService _batchService;
    private readonly ILogger<ErrorLogIndexModel> _logger;

    public ErrorLogIndexModel(
        IErrorLogParserService parserService,
        IErrorLogImportService importService,
        IErrorLogBatchService batchService,
        ILogger<ErrorLogIndexModel> logger)
    {
        _parserService = parserService;
        _importService = importService;
        _batchService = batchService;
        _logger = logger;
    }

    [BindProperty]
    public IReadOnlyList<IFormFile> UploadFiles { get; set; } = [];

    [BindProperty]
    public DateOnly? ErrorLogDate { get; set; }

    [BindProperty]
    public string? ErrorLogMonth { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
        SetDefaults();
    }

    public async Task<IActionResult> OnPostLoadErrorLogAsync(CancellationToken cancellationToken)
    {
        if (ErrorLogDate is null)
        {
            ErrorMessage = "Vui lòng chọn ngày ErrorLog.";
            return RedirectToPage();
        }

        try
        {
            var outputFilePath = await _batchService.RunAsync(ErrorLogDate.Value, cancellationToken);
            await using var stream = System.IO.File.OpenRead(outputFilePath);
            var fileName = Path.GetFileName(outputFilePath);
            var entries = await _parserService.ParseAsync(stream, fileName, cancellationToken);
            var savedCount = await _importService.ReplaceEntriesFromFileAsync(
                entries,
                fileName,
                Guid.NewGuid().ToString("N"),
                DateTime.Now,
                cancellationToken);

            StatusMessage = $"Đã tổng hợp ErrorLog ngày {ErrorLogDate.Value:dd/MM/yyyy}: đọc {entries.Count:N0} dòng, lưu {savedCount:N0} dòng.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load error log for {ErrorLogDate}.", ErrorLogDate);
            ErrorMessage = $"Không thể tổng hợp ErrorLog: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLoadErrorLogMonthAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ErrorLogMonth) ||
            !DateOnly.TryParse($"{ErrorLogMonth}-01", out var selectedMonth))
        {
            ErrorMessage = "Vui lòng chọn tháng ErrorLog.";
            return RedirectToPage();
        }

        try
        {
            var outputFilePath = await _batchService.RunMonthAsync(selectedMonth, cancellationToken);
            await using var stream = System.IO.File.OpenRead(outputFilePath);
            var fileName = Path.GetFileName(outputFilePath);
            var entries = await _parserService.ParseAsync(stream, fileName, cancellationToken);
            var savedCount = await _importService.SaveNewEntriesAsync(
                entries,
                fileName,
                Guid.NewGuid().ToString("N"),
                DateTime.Now,
                cancellationToken);

            StatusMessage = $"Đã tổng hợp ErrorLog tháng {selectedMonth:MM/yyyy}: đọc {entries.Count:N0} dòng, lưu mới {savedCount:N0} dòng.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load error log month {ErrorLogMonth}.", ErrorLogMonth);
            ErrorMessage = $"Không thể tổng hợp ErrorLog theo tháng: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (UploadFiles.Count == 0)
        {
            ErrorMessage = "Vui lòng chọn ít nhất một file ErrorLog.";
            return RedirectToPage();
        }

        try
        {
            var parsedCount = 0;
            var savedCount = 0;
            var batchId = Guid.NewGuid().ToString("N");

            foreach (var file in UploadFiles.Where(file => file.Length > 0))
            {
                await using var stream = file.OpenReadStream();
                var entries = await _parserService.ParseAsync(stream, file.FileName, cancellationToken);
                parsedCount += entries.Count;
                savedCount += await _importService.SaveNewEntriesAsync(
                    entries,
                    file.FileName,
                    batchId,
                    DateTime.Now,
                    cancellationToken);
            }

            StatusMessage = $"Đã tải ErrorLog: đọc {parsedCount:N0} dòng, lưu mới {savedCount:N0} dòng.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload error log files.");
            ErrorMessage = $"Không thể tải ErrorLog: {ex.Message}";
        }

        return RedirectToPage();
    }

    private void SetDefaults()
    {
        ErrorLogDate ??= DateOnly.FromDateTime(DateTime.Today);
        ErrorLogMonth ??= DateTime.Today.ToString("yyyy-MM");
    }
}
