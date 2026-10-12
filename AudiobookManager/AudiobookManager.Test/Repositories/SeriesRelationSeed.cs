using AudiobookManager.Database.Models;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Repository tests seed <see cref="Audiobook"/> rows directly, bypassing
/// <c>AudiobookService</c>. The entity's constructor turns a series argument into its primary
/// <c>audiobook_series</c> relation (the only place the series is stored), so this is now a no-op
/// kept for the many call sites.
/// </summary>
internal static class SeriesRelationSeed
{
    public static Audiobook WithPrimaryRelation(this Audiobook book) => book;
}
