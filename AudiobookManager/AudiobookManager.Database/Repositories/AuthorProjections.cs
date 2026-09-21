namespace AudiobookManager.Database.Repositories;

/// <summary>
/// An author reduced to what list/search views actually render. Loading the full
/// <see cref="Models.Person"/> graph with its BooksAuthored collection just to read
/// <c>BooksAuthored.Count</c> materializes every audiobook row - descriptions included - once
/// per author, so the count is projected in SQL instead.
///
/// <see cref="IsMatched"/>/<see cref="MatchedSourceName"/> mirror
/// <c>Person.MatchedSourceId</c>/<c>Person.MatchedSourceName</c> (see that model's doc) for the
/// author-list match badge (mirrors <c>SeriesOverviewDto.IsMatched</c>/<c>MatchedSourceName</c>).
/// The secondary 3-arg constructor keeps every caller that doesn't care about match state (the
/// narrator/autocomplete projections, which reuse this same row shape via
/// <c>PersonRepository.AuthorSummaryProjection</c>) compiling unchanged, defaulting to unmatched.
/// </summary>
public record AuthorSummaryRow(long Id, string Name, int BookCount, bool IsMatched, string? MatchedSourceName)
{
    public AuthorSummaryRow(long id, string name, int bookCount)
        : this(id, name, bookCount, false, null)
    {
    }
}
