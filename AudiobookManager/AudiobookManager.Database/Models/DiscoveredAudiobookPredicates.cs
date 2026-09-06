using System.Linq.Expressions;

namespace AudiobookManager.Database.Models;

public static class DiscoveredAudiobookPredicates
{
    public static readonly Expression<Func<DiscoveredAudiobook, bool>> IsWellTaggedExpression =
        discovered => !string.IsNullOrWhiteSpace(discovered.Authors)
            && !string.IsNullOrWhiteSpace(discovered.BookName)
            && discovered.Year.HasValue;

    private static readonly Func<DiscoveredAudiobook, bool> IsWellTaggedFunc =
        IsWellTaggedExpression.Compile();

    public static bool IsWellTagged(DiscoveredAudiobook discovered) => IsWellTaggedFunc(discovered);
}
