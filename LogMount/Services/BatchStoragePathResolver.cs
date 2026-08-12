namespace LogMount.Services;

/// <summary>
/// Resolves configured batch paths to shared LOG folders on fixed drives.
/// </summary>
public interface IBatchStoragePathResolver
{
    string Resolve(string configuredPath);
}

public sealed class BatchStoragePathResolver : IBatchStoragePathResolver
{
    private static readonly string[] ExistingPathDriveRoots = ["E:\\", "D:\\", "C:\\"];
    private static readonly string[] NewPathDriveRoots = ["D:\\", "C:\\", "E:\\"];

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
            throw new InvalidOperationException("Chua cau hinh duong dan file batch.");
        }

        if (!Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configuredPath));
        }

        var root = Path.GetPathRoot(configuredPath);
        if (string.IsNullOrWhiteSpace(root) || !root.EndsWith(@":\", StringComparison.Ordinal))
        {
            return configuredPath;
        }

        var suffix = configuredPath[root.Length..];
        var existingPath = ExistingPathDriveRoots
            .Select(driveRoot => driveRoot + suffix)
            .FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            return existingPath;
        }

        var fallbackPath = NewPathDriveRoots
            .Where(Directory.Exists)
            .Select(driveRoot => driveRoot + suffix)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(fallbackPath))
        {
            return configuredPath;
        }

        _logger.LogWarning(
            "Configured path {ConfiguredPath} was not found on E/D/C. Using shared fallback {FallbackPath}.",
            configuredPath,
            fallbackPath);

        return fallbackPath;
    }
}
