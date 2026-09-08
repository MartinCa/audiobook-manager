using System.Linq.Expressions;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;

public class MissingTagService : IMissingTagService
{
    private readonly IAudiobookRepository _audiobookRepository;

    /// <summary>
    /// Description of one taggable field: its key/label (what the frontend fetches from
    /// <c>GET api/missing-tags/fields</c>), plus the two representations of "this field is
    /// missing" - <see cref="IsMissing"/> against the compact per-book projection and
    /// <see cref="IsMissingSql"/>, the same predicate as an SQL-expressible expression the
    /// repository applies as a WHERE clause. The two must say the same thing; the repository
    /// integration tests pin them together against real SQLite.
    /// </summary>
    private record MissingTagFieldDefinition(
        string Key,
        string Label,
        bool IsCriticalByDefault,
        Func<MissingTagRow, bool> IsMissing,
        Expression<Func<DbAudiobook, bool>> IsMissingSql)
    { }

    // The frontend fetches these keys/labels from GET api/missing-tags/fields rather than
    // hardcoding them, so this is the single source of truth. Order here is the display order.
    private static readonly List<MissingTagFieldDefinition> Fields = new()
    {
        new("Authors", "Author", true,
            r => !r.HasRealAuthor,
            a => !a.Authors.Any(p => p.Name != null && p.Name.Trim() != "")),
        new("BookName", "Book Name", true,
            r => r.BookNameBlank,
            a => a.BookName == null || a.BookName.Trim() == ""),
        new("Year", "Year", true,
            r => r.YearZero,
            a => a.Year == 0),
        new("Series", "Series", false,
            r => r.SeriesBlank,
            a => a.Series == null || a.Series.Trim() == ""),
        new("SeriesPart", "Series Part", false,
            r => r.SeriesPartBlank,
            a => a.SeriesPart == null || a.SeriesPart.Trim() == ""),
        new("Narrators", "Narrators", false,
            r => !r.HasRealNarrator,
            a => !a.Narrators.Any(p => p.Name != null && p.Name.Trim() != "")),
        new("Subtitle", "Subtitle", false,
            r => r.SubtitleBlank,
            a => a.Subtitle == null || a.Subtitle.Trim() == ""),
        new("Description", "Description", false,
            r => r.DescriptionBlank,
            a => a.Description == null || a.Description.Trim() == ""),
        new("Genres", "Genres", false,
            r => !r.HasGenre,
            a => !a.Genres.Any()),
        new("Language", "Language", false,
            r => r.LanguageBlank,
            a => a.Language == null || a.Language.Trim() == ""),
        new("Cover", "Cover", false,
            r => r.CoverBlank,
            a => a.CoverFilePath == null || a.CoverFilePath.Trim() == ""),
        new("Copyright", "Copyright", false,
            r => r.CopyrightBlank,
            a => a.Copyright == null || a.Copyright.Trim() == ""),
        new("Publisher", "Publisher", false,
            r => r.PublisherBlank,
            a => a.Publisher == null || a.Publisher.Trim() == ""),
        new("Rating", "Rating", false,
            r => r.RatingBlank,
            a => a.Rating == null || a.Rating.Trim() == ""),
        new("Asin", "ASIN", false,
            r => r.AsinBlank,
            a => a.Asin == null || a.Asin.Trim() == ""),
        new("Www", "Website", false,
            r => r.WwwBlank,
            a => a.Www == null || a.Www.Trim() == ""),
    };

    public MissingTagService(IAudiobookRepository audiobookRepository)
    {
        _audiobookRepository = audiobookRepository;
    }

    public List<MissingTagField> GetTaggableFields() =>
        Fields.Select(f => new MissingTagField(f.Key, f.Label, f.IsCriticalByDefault)).ToList();

    public async Task<(List<AudiobookMissingTags> Items, int Total)> FindAudiobooksMissingTagsPageAsync(
        IEnumerable<string> fieldKeys, string? search, int skip, int take)
    {
        var requestedFields = Fields.Where(f => fieldKeys.Contains(f.Key)).ToList();
        if (requestedFields.Count == 0)
        {
            return (new List<AudiobookMissingTags>(), 0);
        }

        // The selected fields' "is missing" predicates, the search and the page boundaries all
        // run in SQL: the repository applies the predicates as a WHERE clause (an OR of
        // EXISTS/negated-EXISTS subqueries and blank checks), folds the search accent-insensitively
        // and counts/slices the filtered set. Only the returned page's rows are materialized, and
        // only they are evaluated for the per-book MissingFields list.
        var (rows, total) = await _audiobookRepository.GetMissingTagRowsPageAsync(
            requestedFields.Select(f => f.IsMissingSql).ToList(), search, skip, take);

        var results = rows
            .Select(row => (
                Row: row,
                Missing: requestedFields.Where(f => f.IsMissing(row)).Select(f => f.Key).ToList()))
            // The SQL WHERE already guarantees at least one selected field is missing; the guard
            // is a defensive backstop in case the two predicate forms ever drift apart.
            .Where(r => r.Missing.Count > 0)
            .Select(r => new AudiobookMissingTags(r.Row.Id, r.Row.BookName, r.Row.Authors, r.Missing))
            .ToList();

        return (results, total);
    }
}