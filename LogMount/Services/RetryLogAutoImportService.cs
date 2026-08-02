namespace LogMount.Services;

public class RetryLogAutoImportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RetryLogAutoImportService> _logger;

    public RetryLogAutoImportService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RetryLogAutoImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("RetryLogBatch:AutoImportEnabled", true))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayToNextRun(DateTime.Now);
            _logger.LogInformation(
                "Next auto import retry log will run at {NextRunTime}.",
                DateTime.Now.Add(delay));

            await Task.Delay(delay, stoppingToken);
            await ImportTodayAsync(stoppingToken);
        }
    }

    private static TimeSpan GetDelayToNextRun(DateTime now)
    {
        var currentSlot = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var nextRun = now.Minute == 0 && now.Second == 0
            ? currentSlot
            : currentSlot.AddHours(1);

        return nextRun - now;
    }

    private async Task ImportTodayAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var batchService = scope.ServiceProvider.GetRequiredService<IRetryLogBatchService>();
            var parserService = scope.ServiceProvider.GetRequiredService<IRetryLogParserService>();
            var importService = scope.ServiceProvider.GetRequiredService<IRetryLogImportService>();

            var date = DateOnly.FromDateTime(DateTime.Today);
            var outputFilePath = await batchService.RunAsync(date, cancellationToken);
            await using var stream = File.OpenRead(outputFilePath);
            var fileName = Path.GetFileName(outputFilePath);
            var entries = await parserService.ParseAsync(stream, fileName, cancellationToken);

            var savedCount = await importService.ReplaceEntriesFromFileAsync(
                entries,
                fileName,
                Guid.NewGuid().ToString("N"),
                DateTime.Now,
                cancellationToken);

            _logger.LogInformation(
                "Auto imported retry log for {Date}. Parsed {ParsedCount} rows, replaced {SavedCount} rows.",
                date,
                entries.Count,
                savedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto import retry log failed.");
        }
    }
}
