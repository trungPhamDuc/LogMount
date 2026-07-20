using LogMount.Models;
using Microsoft.Extensions.Caching.Memory;

namespace LogMount.Services;

public interface ILogDataStore
{
    void Save(string sessionId, IReadOnlyList<RetryLogEntry> entries, IReadOnlyList<string> fileNames);
    LogDataSession? Get(string sessionId);
    void Clear(string sessionId);
}

public class LogDataSession
{
    public string FileName { get; set; } = string.Empty;
    public IReadOnlyList<string> FileNames { get; set; } = [];
    public IReadOnlyList<RetryLogEntry> Entries { get; set; } = [];
    public DateTime UploadedAt { get; set; }
}

public class MemoryLogDataStore : ILogDataStore
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(2);

    public MemoryLogDataStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Save(string sessionId, IReadOnlyList<RetryLogEntry> entries, IReadOnlyList<string> fileNames)
    {
        var session = new LogDataSession
        {
            FileName = string.Join(", ", fileNames),
            FileNames = fileNames.ToList(),
            Entries = entries.ToList(),
            UploadedAt = DateTime.Now
        };

        _cache.Set(GetCacheKey(sessionId), session, CacheDuration);
    }

    public LogDataSession? Get(string sessionId)
    {
        return _cache.Get<LogDataSession>(GetCacheKey(sessionId));
    }

    public void Clear(string sessionId)
    {
        _cache.Remove(GetCacheKey(sessionId));
    }

    private static string GetCacheKey(string sessionId) => $"log-data:{sessionId}";
}
