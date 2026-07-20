namespace LogMount.Services;

public interface IPartListParserService
{
    Task<IReadOnlyList<string>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}
