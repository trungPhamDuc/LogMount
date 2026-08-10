namespace LogMount.Services;

/// <summary>Creates the local batch scripts and output directories when the web app starts.</summary>
public sealed class BatchInfrastructureInitializer : IHostedService
{
    private readonly IRetryLogBatchService _retryLogBatchService;
    private readonly IErrorLogBatchService _errorLogBatchService;
    private readonly ILogger<BatchInfrastructureInitializer> _logger;

    public BatchInfrastructureInitializer(
        IRetryLogBatchService retryLogBatchService,
        IErrorLogBatchService errorLogBatchService,
        ILogger<BatchInfrastructureInitializer> logger)
    {
        _retryLogBatchService = retryLogBatchService;
        _errorLogBatchService = errorLogBatchService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            await _retryLogBatchService.EnsureInfrastructureAsync(today, cancellationToken);
            await _errorLogBatchService.EnsureInfrastructureAsync(today, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not initialize the batch file infrastructure.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
