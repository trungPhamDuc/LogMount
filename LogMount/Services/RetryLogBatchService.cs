using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LogMount.Services;

public interface IRetryLogBatchService
{
    Task<string> RunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<string> RunMonthAsync(DateOnly month, CancellationToken cancellationToken = default);
}

public class RetryLogBatchService : IRetryLogBatchService
{
    private static readonly Regex DailyRetryLogNamePattern = new(
        @"RetryLog\d{8}\.csv",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MonthlyRetryLogNamePattern = new(
        @"RetryLog\d{6}\*\*\.csv",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OutputRetryLogNamePattern = new(
        @"TotalRetryLog(?:\d{8}|\d{6}\*\*|\d{6}|\d{2})\.csv",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PauseCommandPattern = new(
        @"(?im)^\s*pause\s*$",
        RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim BatchLock = new(1, 1);

    private readonly IConfiguration _configuration;

    public RetryLogBatchService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> RunAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var dateText = date.ToString("yyyyMMdd");
        var outputFilePath = ResolveOutputFilePath("OutputFileTemplate", dateText, date.ToString("yyyyMM"), date.ToString("MM"));

        return await RunInternalAsync($"RetryLog{dateText}.csv", outputFilePath, cancellationToken);
    }

    public async Task<string> RunMonthAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        var monthText = month.ToString("yyyyMM");
        var outputFilePath = ResolveOutputFilePath("MonthlyOutputFileTemplate", monthText, monthText, month.ToString("MM"));

        return await RunInternalAsync($"RetryLog{monthText}**.csv", outputFilePath, cancellationToken);
    }

    private string ResolveOutputFilePath(string templateKey, string dateText, string monthText, string monthNumber)
    {
        var outputFileTemplate = _configuration[$"RetryLogBatch:{templateKey}"];
        outputFileTemplate = string.IsNullOrWhiteSpace(outputFileTemplate)
            ? _configuration["RetryLogBatch:OutputFileTemplate"]
            : outputFileTemplate;

        if (string.IsNullOrWhiteSpace(outputFileTemplate))
        {
            throw new InvalidOperationException("Chưa cấu hình RetryLogBatch trong appsettings.json.");
        }

        return outputFileTemplate
            .Replace("{date}", dateText, StringComparison.Ordinal)
            .Replace("{month}", monthText, StringComparison.Ordinal)
            .Replace("{yyyyMM}", monthText, StringComparison.Ordinal)
            .Replace("{MM}", monthNumber, StringComparison.Ordinal);
    }

    private async Task<string> RunInternalAsync(string retryLogFileName, string outputFilePath, CancellationToken cancellationToken)
    {
        var batchFilePath = _configuration["RetryLogBatch:BatchFilePath"];

        if (string.IsNullOrWhiteSpace(batchFilePath))
        {
            throw new InvalidOperationException("Chưa cấu hình RetryLogBatch trong appsettings.json.");
        }

        if (!File.Exists(batchFilePath))
        {
            throw new FileNotFoundException("Không tìm thấy file batch tổng hợp retry log.", batchFilePath);
        }

        await BatchLock.WaitAsync(cancellationToken);
        try
        {
            var batchContent = await File.ReadAllTextAsync(batchFilePath, cancellationToken);
            var updatedBatchContent = DailyRetryLogNamePattern.Replace(batchContent, retryLogFileName);
            updatedBatchContent = MonthlyRetryLogNamePattern.Replace(updatedBatchContent, retryLogFileName);
            updatedBatchContent = OutputRetryLogNamePattern.Replace(
                updatedBatchContent,
                Path.GetFileName(outputFilePath));
            updatedBatchContent = PauseCommandPattern.Replace(updatedBatchContent, string.Empty);

            if (!string.Equals(batchContent, updatedBatchContent, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(batchFilePath, updatedBatchContent, new UTF8Encoding(false), cancellationToken);
            }

            var outputDirectory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(batchFilePath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(batchFilePath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Không thể chạy file batch tổng hợp retry log.");
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"File batch kết thúc với mã lỗi {process.ExitCode}.");
            }

            if (!File.Exists(outputFilePath))
            {
                throw new FileNotFoundException("File retry log tổng hợp không được tạo.", outputFilePath);
            }

            return outputFilePath;
        }
        finally
        {
            BatchLock.Release();
        }
    }
}
