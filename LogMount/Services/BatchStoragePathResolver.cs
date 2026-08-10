namespace LogMount.Services;

/// <summary>
/// Resolves configured batch paths, falling back to the application's LOG folder
/// when the configured drive is unavailable on the current computer.
/// </summary>
public interface IBatchStoragePathResolver
{
    string Resolve(string configuredPath);
}

public sealed class BatchStoragePathResolver : IBatchStoragePathResolver
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<BatchStoragePathResolver> _logger;

    public BatchStoragePathResolver(IHostEnvironment environment, ILogger<BatchStoragePathResolver> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public string Resolve(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("Chưa cấu hình đường dẫn file batch.");
        }

        if (!Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configuredPath));
        }

        var root = Path.GetPathRoot(configuredPath);
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            return configuredPath;
        }

        // E:\LOG\... becomes <application folder>\LOG\... when E: is unavailable.
        var relativePath = string.IsNullOrWhiteSpace(root)
            ? Path.GetFileName(configuredPath)
            : configuredPath[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fallbackPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, relativePath));

        _logger.LogWarning(
            "Configured path {ConfiguredPath} is unavailable. Using local fallback {FallbackPath}.",
            configuredPath,
            fallbackPath);

        return fallbackPath;
    }
}
