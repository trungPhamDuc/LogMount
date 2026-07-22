using LogMount.Models;

namespace LogMount.Services;

public interface IPartListParserService
{
    Task<IReadOnlyList<ExpensivePart>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}
