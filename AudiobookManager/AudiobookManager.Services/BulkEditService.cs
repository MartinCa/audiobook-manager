using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>One change to a single-value field. Clear replaces the value with null.</summary>
public sealed record BulkSingleChange(string? Value, bool Clear);

/// <summary>One change to the Year field; the value is an int rather than free text.</summary>
public sealed record BulkIntChange(int? Value, bool Clear);

/// <summary>
/// One change to a multi-value field. Mode is "replace" or "add"; <see cref="Clear"/> (with Mode
/// null) empties the list - the explicit way to clear narrators/genres across the selection.
/// </summary>
public sealed record BulkMultiChange(string? Mode, List<string> Values, bool Clear = false);

/// <summary>
/// The complete set of field changes for one bulk edit. Every field is independently optional,
/// and a field with no change object is left untouched by design - there is no implicit clearing.
/// The controller builds one of these from the request DTO after validation; the service applies
/// it to every selected book.
/// </summary>
public sealed class AudiobookBulkChanges
{
    public BulkSingleChange? BookName;
    public BulkSingleChange? Subtitle;
    public BulkSingleChange? Series;
    public BulkSingleChange? SeriesPart;
    public BulkSingleChange? Description;
    public BulkSingleChange? Copyright;
    public BulkSingleChange? Publisher;
    public BulkSingleChange? Language;
    public BulkSingleChange? Rating;
    public BulkSingleChange? Asin;
    public BulkSingleChange? Www;
    public BulkIntChange? Year;
    public BulkMultiChange? Authors;
    public BulkMultiChange? Narrators;
    public BulkMultiChange? Genres;

    public bool IsEmpty =>
        BookName is null && Subtitle is null && Series is null && SeriesPart is null && Description is null
        && Copyright is null && Publisher is null && Language is null && Rating is null && Asin is null && Www is null
        && Year is null && Authors is null && Narrators is null && Genres is null;

    /// <summary>
    /// Applies every present change to <paramref name="book"/>. An absent change leaves the
    /// field completely untouched - there is no implicit clearing.
    /// </summary>
    public void ApplyTo(Audiobook book)
    {
        if (BookName != null) book.BookName = ApplySingle(BookName);
        if (Subtitle != null) book.Subtitle = ApplySingle(Subtitle);
        if (Series != null) book.Series = ApplySingle(Series);
        if (SeriesPart != null) book.SeriesPart = ApplySingle(SeriesPart);
        if (Description != null) book.Description = ApplySingle(Description);
        if (Copyright != null) book.Copyright = ApplySingle(Copyright);
        if (Publisher != null) book.Publisher = ApplySingle(Publisher);
        if (Language != null) book.Language = ApplySingle(Language);
        if (Rating != null) book.Rating = ApplySingle(Rating);
        if (Asin != null) book.Asin = ApplySingle(Asin);
        if (Www != null) book.Www = ApplySingle(Www);
        if (Year != null) book.Year = Year.Clear ? null : Year.Value;

        if (Authors != null)
        {
            book.Authors = MergeNames(book.Authors.Select(a => a.Name).ToList(), Authors)
                .Select(name => new Person(name)).ToList();
        }
        if (Narrators != null)
        {
            book.Narrators = MergeNames(book.Narrators.Select(n => n.Name).ToList(), Narrators)
                .Select(name => new Person(name)).ToList();
        }
        if (Genres != null)
        {
            book.Genres = MergeNames(book.Genres, Genres);
        }
    }

    /// <summary>set stores the trimmed value; clear stores null.</summary>
    private static string? ApplySingle(BulkSingleChange change) =>
        change.Clear ? null : change.Value?.Trim();

    /// <summary>
    /// replace assigns the incoming list as the new value; add appends only the values not
    /// already present, compared case-insensitively, preserving the existing order and then the
    /// new values in the order given. A clear change assigns an empty list.
    /// </summary>
    private static List<string> MergeNames(List<string> existing, BulkMultiChange change)
    {
        if (change.Clear)
        {
            return new List<string>();
        }

        if (change.Mode == "replace")
        {
            return change.Values;
        }

        var result = new List<string>(existing);
        foreach (var value in change.Values)
        {
            var alreadyPresent = result.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
            if (!alreadyPresent)
            {
                result.Add(value);
            }
        }

        return result;
    }
}

public interface IBulkEditService
{
    /// <summary>
    /// The selected books' domain objects, for the preview the client shows before the edit runs.
    /// Books whose id does not resolve are simply absent from the result.
    /// </summary>
    Task<List<Audiobook>> GetPreviewAsync(IReadOnlyList<long> ids);

    /// <summary>
    /// Applies <paramref name="changes"/> to every resolved book in <paramref name="audiobookIds"/>,
    /// taking the per-audiobook save gate per book (a busy book fails just its own item and the
    /// batch carries on). Books requested but not found never appear in the loaded set, so they are
    /// not counted. Public author/series changes invalidate the similar-value detection cache after
    /// the batch.
    /// </summary>
    Task<(int Processed, int Succeeded, int Failed)> ApplyAsync(
        AudiobookBulkChanges changes,
        IReadOnlyList<long> audiobookIds,
        Func<int, int, int, int, Task> progressAction);
}

public class BulkEditService : IBulkEditService
{
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly ILibraryConsistencyService _libraryConsistencyService;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ISimilarValueDetectionCache _detectionCache;
    private readonly ILogger<BulkEditService> _logger;

    public BulkEditService(
        IAudiobookRepository audiobookRepository,
        IAudiobookService audiobookService,
        ILibraryConsistencyService libraryConsistencyService,
        IAudiobookSaveGate saveGate,
        ISimilarValueDetectionCache detectionCache,
        ILogger<BulkEditService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _audiobookService = audiobookService;
        _libraryConsistencyService = libraryConsistencyService;
        _saveGate = saveGate;
        _detectionCache = detectionCache;
        _logger = logger;
    }

    public async Task<List<Audiobook>> GetPreviewAsync(IReadOnlyList<long> ids)
    {
        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(ids);
        return books.Select(b => AudiobookService.FromDb(b)).ToList();
    }

    public async Task<(int Processed, int Succeeded, int Failed)> ApplyAsync(
        AudiobookBulkChanges changes,
        IReadOnlyList<long> audiobookIds,
        Func<int, int, int, int, Task> progressAction)
    {
        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(audiobookIds);

        var result = await BulkOperationRunner.RunAsync(
            books,
            async dbBook =>
            {
                // A bulk edit rewrites tags and can relocate the file, exactly like an interactive
                // save - so it takes the one per-audiobook gate. A book someone is saving right
                // now fails just its own item; BulkOperationRunner counts it and the batch carries
                // on (the same shape as the similar-value alignment loops).
                using var lease = _saveGate.Acquire(dbBook.Id);

                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;
                changes.ApplyTo(domain);

                // Defensive, and deliberately not reachable through this controller's validation:
                // a bulk edit must never persist a book with no author or title, whatever caller
                // (or bug) produced the change set.
                if (domain.Authors.Count == 0 || string.IsNullOrWhiteSpace(domain.BookName))
                {
                    throw new Exception(
                        $"Bulk edit would leave audiobook {dbBook.Id} without an author or a title; the edit was refused.");
                }

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);

                try
                {
                    await _libraryConsistencyService.RecheckAudiobookAsync(dbBook.Id);
                }
                catch (Exception ex)
                {
                    // Mirrors the save endpoint: a recheck failure is non-fatal. It keeps the
                    // issue badges truthful after a bulk edit, but a consistency-check bug must
                    // not fail the edit itself.
                    _logger.LogWarning(ex, "Failed to recheck consistency issues for audiobook {AudiobookId} after bulk edit", dbBook.Id);
                }
            },
            _logger,
            dbBook => $"Failed to apply bulk edit to audiobook {dbBook.Id}",
            progressAction);

        // The bulk edit can merge author/series values; the cached detection grouping must not
        // keep serving pre-edit groups until the TTL runs out.
        if (changes.Authors is not null || changes.Series is not null)
        {
            _detectionCache.Invalidate();
        }

        return result;
    }
}