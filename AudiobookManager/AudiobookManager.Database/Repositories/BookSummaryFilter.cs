namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Additional narrowing for <see cref="IAudiobookRepository.GetAllAsync"/> and
/// <see cref="IAudiobookRepository.SearchAsync"/>, the book-list counterpart of
/// <see cref="AuthorSummaryFilter"/>/<see cref="SeriesOverviewFilter"/>. Every field is optional
/// and independent - a null/empty field applies no constraint.
/// </summary>
public record BookSummaryFilter(
    IReadOnlyCollection<string>? Sources = null,
    IReadOnlyCollection<string>? Genres = null,
    IReadOnlyCollection<string>? Languages = null,
    int? MinDurationInSeconds = null,
    int? MaxDurationInSeconds = null)
{
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
        MinDurationInSeconds is null && MaxDurationInSeconds is null;
}
