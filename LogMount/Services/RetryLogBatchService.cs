using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LogMount.Services;

public interface IRetryLogBatchService
{
    Task<string> RunAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public class RetryLogBatchService : IRetryLogBatchService
{
    private static readonly Regex RetryLogNamePattern = new(
        @"RetryLog\d{8}\.csv",
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
        var batchFilePath = _configuration["RetryLogBatch:BatchFilePath"];
        var outputFileTemplate = _configuration["RetryLogBatch:OutputFileTemplate"];

        if (string.IsNullOrWhiteSpace(batchFilePath) || string.IsNullOrWhiteSpace(outputFileTemplate))
        {
            throw new InvalidOperationException("Chưa cấu hình RetryLogBatch trong appsettings.json.");
        }

        if (!File.Exists(batchFilePath))
        {
            throw new FileNotFoundException("Không tìm thấy file batch tổng hợp retry log.", batchFilePath);
        }

        var dateText = date.ToString("yyyyMMdd");
        var outputFilePath = outputFileTemplate.Replace("{date}", dateText, StringComparison.Ordinal);

        await BatchLock.WaitAsync(cancellationToken);
        try
        {
            var batchContent = await File.ReadAllTextAsync(batchFilePath, cancellationToken);
            var updatedBatchContent = RetryLogNamePattern.Replace(batchContent, $"RetryLog{dateText}.csv");
            updatedBatchContent = PauseCommandPattern.Replace(updatedBatchContent, string.Empty);

            if (!string.Equals(batchContent, updatedBatchContent, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(batchFilePath, updatedBatchContent, new UTF8Encoding(false), cancellationToken);
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
