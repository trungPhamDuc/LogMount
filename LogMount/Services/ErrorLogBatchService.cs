using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LogMount.Services;

public interface IErrorLogBatchService
{
    Task<string> RunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<string> RunMonthAsync(DateOnly month, CancellationToken cancellationToken = default);
    Task EnsureInfrastructureAsync(DateOnly date, CancellationToken cancellationToken = default);
}

public class ErrorLogBatchService : IErrorLogBatchService
{
    private static readonly Regex DailyErrorLogNamePattern = new(
        @"(?:ErrorLog|ErrLog)\d{8}\.(?:csv|xlsx|xls)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MonthlyErrorLogNamePattern = new(
        @"(?:ErrorLog|ErrLog)\d{6}\*\*\.(?:csv|xlsx|xls)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OutputErrorLogNamePattern = new(
        @"(?:TotalErrorLog|TotalErrLog)(?:\d{8}|\d{6}\*\*|\d{6}|\d{2})\.(?:csv|xlsx|xls)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PauseCommandPattern = new(
        @"(?im)^\s*pause\s*$",
        RegexOptions.CultureInvariant);
    private static readonly SemaphoreSlim BatchLock = new(1, 1);

    private readonly IConfiguration _configuration;
    private readonly IBatchStoragePathResolver _pathResolver;

    public ErrorLogBatchService(IConfiguration configuration, IBatchStoragePathResolver pathResolver)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
    }

    public async Task<string> RunAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var dateText = date.ToString("yyyyMMdd");
        var outputFilePath = ResolveOutputFilePath("OutputFileTemplate", dateText, date.ToString("yyyyMM"), date.ToString("MM"));

        return await RunInternalAsync($"ErrLog{dateText}.csv", outputFilePath, cancellationToken);
    }

    public async Task<string> RunMonthAsync(DateOnly month, CancellationToken cancellationToken = default)
    {
        var monthText = month.ToString("yyyyMM");
        var outputFilePath = ResolveOutputFilePath("MonthlyOutputFileTemplate", monthText, monthText, month.ToString("MM"));

        return await RunInternalAsync($"ErrLog{monthText}**.csv", outputFilePath, cancellationToken);
    }

    public async Task EnsureInfrastructureAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var dateText = date.ToString("yyyyMMdd");
        var outputFilePath = ResolveOutputFilePath("OutputFileTemplate", dateText, date.ToString("yyyyMM"), date.ToString("MM"));
        var batchFilePath = ResolveBatchFilePath();

        var batchDirectory = Path.GetDirectoryName(batchFilePath);
        if (!string.IsNullOrWhiteSpace(batchDirectory))
        {
            Directory.CreateDirectory(batchDirectory);
        }

        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (!File.Exists(batchFilePath))
        {
            await CreateDefaultBatchFileAsync(batchFilePath, $"ErrLog{dateText}.csv", outputFilePath, cancellationToken);
        }
    }

    private string ResolveOutputFilePath(string templateKey, string dateText, string monthText, string monthNumber)
    {
        var outputFileTemplate = _configuration[$"ErrorLogBatch:{templateKey}"];
        outputFileTemplate = string.IsNullOrWhiteSpace(outputFileTemplate)
            ? _configuration["ErrorLogBatch:OutputFileTemplate"]
            : outputFileTemplate;

        if (string.IsNullOrWhiteSpace(outputFileTemplate))
        {
            throw new InvalidOperationException("Chưa cấu hình ErrorLogBatch trong appsettings.json.");
        }

        return _pathResolver.Resolve(outputFileTemplate
            .Replace("{date}", dateText, StringComparison.Ordinal)
            .Replace("{month}", monthText, StringComparison.Ordinal)
            .Replace("{yyyyMM}", monthText, StringComparison.Ordinal)
            .Replace("{MM}", monthNumber, StringComparison.Ordinal));
    }

    private string ResolveBatchFilePath() => _pathResolver.Resolve(_configuration["ErrorLogBatch:BatchFilePath"] ?? string.Empty);

    private async Task<string> RunInternalAsync(string errorLogFileName, string outputFilePath, CancellationToken cancellationToken)
    {
        var batchFilePath = ResolveBatchFilePath();

        await BatchLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(batchFilePath) || await IsGeneratedSingleSourceBatchAsync(batchFilePath, cancellationToken))
            {
                await CreateDefaultBatchFileAsync(batchFilePath, errorLogFileName, outputFilePath, cancellationToken);
            }

            var batchContent = await File.ReadAllTextAsync(batchFilePath, cancellationToken);
            var updatedBatchContent = DailyErrorLogNamePattern.Replace(batchContent, errorLogFileName);
            updatedBatchContent = MonthlyErrorLogNamePattern.Replace(updatedBatchContent, errorLogFileName);
            updatedBatchContent = OutputErrorLogNamePattern.Replace(
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
                ?? throw new InvalidOperationException("Không thể chạy file batch tổng hợp error log.");
            await process.WaitForExitAsync(cancellationToken);

            if (!File.Exists(outputFilePath))
            {
                await File.WriteAllTextAsync(outputFilePath, string.Empty, new UTF8Encoding(false), cancellationToken);
            }

            if (process.ExitCode != 0 && new FileInfo(outputFilePath).Length > 0)
            {
                throw new InvalidOperationException($"File batch kết thúc với mã lỗi {process.ExitCode}.");
            }

            return outputFilePath;
        }
        finally
        {
            BatchLock.Release();
        }
    }

    private async Task CreateDefaultBatchFileAsync(
        string batchFilePath,
        string errorLogFileName,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        var batchDirectory = Path.GetDirectoryName(batchFilePath);
        if (!string.IsNullOrWhiteSpace(batchDirectory))
        {
            Directory.CreateDirectory(batchDirectory);
        }

        var retryLogBatchFilePath = _pathResolver.Resolve(_configuration["RetryLogBatch:BatchFilePath"] ?? string.Empty);
        var content = !string.IsNullOrWhiteSpace(retryLogBatchFilePath) && File.Exists(retryLogBatchFilePath)
            ? await CreateBatchFromRetryLogTemplateAsync(retryLogBatchFilePath, cancellationToken)
            : CreateMinimalBatchContent(errorLogFileName, outputFilePath);

        await File.WriteAllTextAsync(batchFilePath, content, new UTF8Encoding(false), cancellationToken);
    }

    private static async Task<bool> IsGeneratedSingleSourceBatchAsync(string batchFilePath, CancellationToken cancellationToken)
    {
        var batchContent = await File.ReadAllTextAsync(batchFilePath, cancellationToken);
        return batchContent.Contains(@"set ""SOURCE=%~dp0", StringComparison.OrdinalIgnoreCase) &&
               batchContent.Contains(@"copy /b ""%SOURCE%"" ""%OUTPUT%""", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> CreateBatchFromRetryLogTemplateAsync(
        string retryLogBatchFilePath,
        CancellationToken cancellationToken)
    {
        var retryLogBatchContent = await File.ReadAllTextAsync(retryLogBatchFilePath, cancellationToken);
        return retryLogBatchContent
            .Replace("Total RetryLog", "Total ErrorLog", StringComparison.OrdinalIgnoreCase)
            .Replace("TotalRetryLog", "TotalErrorLog", StringComparison.OrdinalIgnoreCase)
            .Replace("RetryLog", "ErrorLog", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateMinimalBatchContent(string errorLogFileName, string outputFilePath)
    {
        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        var createOutputDirectoryCommand = string.IsNullOrWhiteSpace(outputDirectory)
            ? string.Empty
            : $"""if not exist "{outputDirectory}" mkdir "{outputDirectory}" """;
        var sourcePattern = errorLogFileName.Replace("**", "*", StringComparison.Ordinal);
        return $"""
@echo off
setlocal
set "SOURCE=%~dp0{sourcePattern}"
set "OUTPUT={outputFilePath}"
{createOutputDirectoryCommand}
copy /b "%SOURCE%" "%OUTPUT%" /y
endlocal
""";
    }
}
