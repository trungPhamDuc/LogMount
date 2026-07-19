using LogMount.Models;

namespace LogMount.Services;

public interface IRetryLogParserService
{
    Task<IReadOnlyList<RetryLogEntry>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}
