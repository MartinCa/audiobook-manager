namespace AudiobookManager.Database.Repositories;

/// <summary>
/// One author attribution for an <see cref="ExpectedBookUpsert"/>: the library person id when it
/// can be resolved, and the author's name as the source reported it (always present - a source
/// author with no local <see cref="AudiobookManager.Database.Models.Person"/> row must not be lost).
/// </summary>
public record ExpectedBookAuthorLink(long? PersonId, string AuthorName);

/// <summary>
/// The refresh payload for one expected book - see
/// <see cref="IExpectedBookRepository.UpsertAsync"/>. <see cref="SourceBookId"/> is the dedup
/// identity: when absent (a poll that somehow lacks the source's book id) no unique-index
/// guarantee exists, but every real scrape path provides it.
/// </summary>
public record ExpectedBookUpsert(
    string SourceName,
    string? SourceBookId,
    string Title,
    int? Year,
    DateOnly? ReleaseDate,
    string? SourceUrl,
    string? ImageUrl,
    long? SeriesId,
    string? SourceSeriesId,
    string? SourceSeriesName,
    string? SeriesPosition,
    // Null marks "this poll supplies no compilation information". The series-shaped caller
    // (SeriesService.MatchSeriesCoreAsync) sets it from the source's flag; the author-shaped
    // caller (UpcomingReleaseService.RefreshAuthorRosterCoreAsync) passes null, so an author
    // refresh must never clear an IsCompilation a series refresh established (a row discovered
    // by both scopes keeps the series' judgement on a field only the series source reports).
    bool? IsCompilation,
    IReadOnlyList<ExpectedBookAuthorLink> Authors);