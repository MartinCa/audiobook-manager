using AudiobookManager.Database.Models;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// Repository tests seed <see cref="Audiobook"/> rows directly, bypassing
/// <c>AudiobookService</c>. In production <c>SeriesRelationSync</c> keeps an
/// <c>audiobook_series</c> row in step with <c>Series</c>/<c>SeriesPart</c>, and every
/// series-keyed query reads those rows - so a seed that sets a series has to add its primary
/// relation the same way.
/// </summary>
internal static class SeriesRelationSeed
{
    public static Audiobook WithPrimaryRelation(this Audiobook book)
    {
        if (!string.IsNullOrWhiteSpace(book.Series))
        {
            book.SeriesRelations = new List<AudiobookSeries>
            {
                new() { SeriesName = book.Series.Trim(), SeriesPart = book.SeriesPart, IsPrimary = true, SortOrder = 0 },
            };
        }

        return book;
    }
}
