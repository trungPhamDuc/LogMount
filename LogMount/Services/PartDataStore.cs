using LogMount.Models;
using Microsoft.Extensions.Caching.Memory;

namespace LogMount.Services;

public interface IPartDataStore
{
    void Save(string sessionId, IReadOnlyList<ExpensivePart> parts, string fileName);
    PartDataSession? Get(string sessionId);
    void Clear(string sessionId);
}

public class PartDataSession
{
    public string FileName { get; set; } = string.Empty;
    public IReadOnlyList<ExpensivePart> Parts { get; set; } = [];
    public DateTime UploadedAt { get; set; }
}

public class MemoryPartDataStore : IPartDataStore
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(2);

    public MemoryPartDataStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Save(string sessionId, IReadOnlyList<ExpensivePart> parts, string fileName)
    {
        var session = new PartDataSession
        {
            FileName = fileName,
            Parts = parts.ToList(),
            UploadedAt = DateTime.Now
        };

        _cache.Set(GetCacheKey(sessionId), session, CacheDuration);
    }

    public PartDataSession? Get(string sessionId)
    {
        return _cache.Get<PartDataSession>(GetCacheKey(sessionId));
    }

    public void Clear(string sessionId)
    {
        _cache.Remove(GetCacheKey(sessionId));
    }

    private static string GetCacheKey(string sessionId) => $"part-data:{sessionId}";
}
