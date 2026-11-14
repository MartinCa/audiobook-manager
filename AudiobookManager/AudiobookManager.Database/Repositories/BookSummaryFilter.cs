namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Additional narrowing for <see cref="IAudiobookRepository.GetAllAsync"/> and
/// <see cref="IAudiobookRepository.SearchAsync"/>, the book-list counterpart of
/// <see cref="AuthorSummaryFilter"/>/<see cref="SeriesOverviewFilter"/>. Every field is optional
/// and independent - a null/empty field applies no constraint. <c>MultipleAuthors</c> true keeps
/// books credited to more than one author and false those with a single one;
/// <c>HasMainAuthor</c> true keeps books that have a main author set and false those without.
/// </summary>
public record BookSummaryFilter(
    IReadOnlyCollection<string>? Sources = null,
    IReadOnlyCollection<string>? Genres = null,
    IReadOnlyCollection<string>? Languages = null,
    int? MinDurationInSeconds = null,
    int? MaxDurationInSeconds = null,
    IReadOnlyCollection<string>? Qualifiers = null,
    DateTime? RefreshedAfter = null,
    DateTime? RefreshedBefore = null,
    bool? NeverRefreshed = null,
    IReadOnlyCollection<string>? QueueStates = null,
    bool? MultipleAuthors = null,
    bool? HasMainAuthor = null)
{
    /// <summary>
    /// Synthetic <see cref="Qualifiers"/> value for a book that carries no qualifier at all. It
    /// is not a valid qualifier key (the registry only holds lowercase alphanumeric keys), so it
    /// can never collide with one.
    /// </summary>
    public const string NoQualifier = "(none)";

    /// <summary>
    /// Synthetic <see cref="Sources"/> value for a book whose Www does not match a known
    /// scraper's source (including having no Www at all) - mirrors
    /// <see cref="AuthorSummaryFilter.UnsupportedSource"/>.
    /// </summary>
    public const string UnsupportedSource = AuthorSummaryFilter.UnsupportedSource;

    public bool IsEmpty =>
        (Sources is null || Sources.Count == 0) &&
        (Genres is null || Genres.Count == 0) &&
        (Languages is null || Languages.Count == 0) &&
        (Qualifiers is null || Qualifiers.Count == 0) &&
        MinDurationInSeconds is null && MaxDurationInSeconds is null &&
        RefreshedAfter is null && RefreshedBefore is null && NeverRefreshed is null &&
        (QueueStates is null || QueueStates.Count == 0) &&
        MultipleAuthors is null && HasMainAuthor is null;
}
