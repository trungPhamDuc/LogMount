using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LogMount.Services;

public interface IRetryLogBatchService
{
    Task<string> RunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<string> RunMonthAsync(DateOnly month, CancellationToken cancellationToken = default);
    Task EnsureInfrastructureAsync(DateOnly date, CancellationToken cancellationToken = default);
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
    private static readonly Regex LocalRetryLogRootPattern = new(
        @"(?<!\\)[A-Z]:\\LOG\\RetryLog",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] FallbackDriveRoots = ["E:\\", "D:\\", "C:\\"];
    private static readonly SemaphoreSlim BatchLock = new(1, 1);

    private readonly IConfiguration _configuration;
    private readonly IBatchStoragePathResolver _pathResolver;

    public RetryLogBatchService(IConfiguration configuration, IBatchStoragePathResolver pathResolver)
    {
        _configuration = configuration;
        _pathResolver = pathResolver;
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
            await CreateDefaultBatchFileAsync(batchFilePath, $"RetryLog{dateText}.csv", outputFilePath, cancellationToken);
        }
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

        return ResolveAvailableDrivePath(_pathResolver.Resolve(outputFileTemplate
            .Replace("{date}", dateText, StringComparison.Ordinal)
            .Replace("{month}", monthText, StringComparison.Ordinal)
            .Replace("{yyyyMM}", monthText, StringComparison.Ordinal)
            .Replace("{MM}", monthNumber, StringComparison.Ordinal)));
    }

    private string ResolveBatchFilePath() => ResolveAvailableDrivePath(_pathResolver.Resolve(_configuration["RetryLogBatch:BatchFilePath"] ?? string.Empty));

    private async Task<string> RunInternalAsync(string retryLogFileName, string outputFilePath, CancellationToken cancellationToken)
    {
        var batchFilePath = ResolveBatchFilePath();

        await BatchLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(batchFilePath) || await IsGeneratedSingleSourceBatchAsync(batchFilePath, cancellationToken))
            {
                await CreateDefaultBatchFileAsync(batchFilePath, retryLogFileName, outputFilePath, cancellationToken);
            }

            var batchContent = await File.ReadAllTextAsync(batchFilePath, cancellationToken);
            var updatedBatchContent = DailyRetryLogNamePattern.Replace(batchContent, retryLogFileName);
            updatedBatchContent = MonthlyRetryLogNamePattern.Replace(updatedBatchContent, retryLogFileName);
            updatedBatchContent = OutputRetryLogNamePattern.Replace(
                updatedBatchContent,
                Path.GetFileName(outputFilePath));
            updatedBatchContent = NormalizeLocalRetryLogRoot(updatedBatchContent, outputFilePath);
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

    private static async Task CreateDefaultBatchFileAsync(
        string batchFilePath,
        string retryLogFileName,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        var batchDirectory = Path.GetDirectoryName(batchFilePath);
        if (!string.IsNullOrWhiteSpace(batchDirectory))
        {
            Directory.CreateDirectory(batchDirectory);
        }

        var templateContent = await TryReadRetryLogBatchTemplateAsync(batchFilePath, cancellationToken);
        var content = templateContent is not null
            ? NormalizeLocalRetryLogRoot(templateContent, outputFilePath)
            : CreateMinimalBatchContent(retryLogFileName, outputFilePath);

        await File.WriteAllTextAsync(batchFilePath, content, new UTF8Encoding(false), cancellationToken);
    }

    private static async Task<bool> IsGeneratedSingleSourceBatchAsync(string batchFilePath, CancellationToken cancellationToken)
    {
        var batchContent = await File.ReadAllTextAsync(batchFilePath, cancellationToken);
        return batchContent.Contains(@"set ""SOURCE=%~dp0", StringComparison.OrdinalIgnoreCase) &&
               batchContent.Contains(@"copy /b ""%SOURCE%"" ""%OUTPUT%""", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> TryReadRetryLogBatchTemplateAsync(
        string targetBatchFilePath,
        CancellationToken cancellationToken)
    {
        foreach (var driveRoot in FallbackDriveRoots.Where(Directory.Exists))
        {
            var templateDirectory = Path.Combine(driveRoot, "LOG", "RetryLog");
            if (!Directory.Exists(templateDirectory))
            {
                continue;
            }

            var candidates = Directory.GetFiles(templateDirectory, "*.bat")
                .OrderByDescending(path => Path.GetFileName(path).Equals("TotalRetryLogDay.bat", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                if (string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(targetBatchFilePath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(candidate, cancellationToken);
                if (IsUsableRetryLogTemplate(content))
                {
                    return content;
                }
            }
        }

        return null;
    }

    private static bool IsUsableRetryLogTemplate(string content)
    {
        return !content.Contains(@"set ""SOURCE=%~dp0", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("RetryLog", StringComparison.OrdinalIgnoreCase) &&
               content.Contains("COPY", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateMinimalBatchContent(string retryLogFileName, string outputFilePath)
    {
        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        var createOutputDirectoryCommand = string.IsNullOrWhiteSpace(outputDirectory)
            ? string.Empty
            : $"""if not exist "{outputDirectory}" mkdir "{outputDirectory}" """;
        var sourcePattern = retryLogFileName.Replace("**", "*", StringComparison.Ordinal);
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

    private static string NormalizeLocalRetryLogRoot(string content, string outputFilePath)
    {
        var driveRoot = Path.GetPathRoot(outputFilePath);
        var drive = !string.IsNullOrWhiteSpace(driveRoot) && driveRoot.Length >= 2
            ? driveRoot[..2]
            : "E:";
        return LocalRetryLogRootPattern.Replace(content, $@"{drive}\LOG\RetryLog");
    }

    private static string ResolveAvailableDrivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            return path;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root) || !root.EndsWith(@":\", StringComparison.Ordinal))
        {
            return path;
        }

        if (Directory.Exists(root))
        {
            return path;
        }

        var suffix = path[root.Length..];
        foreach (var fallbackRoot in FallbackDriveRoots)
        {
            if (Directory.Exists(fallbackRoot))
            {
                return fallbackRoot + suffix;
            }
        }

        return path;
    }
}
