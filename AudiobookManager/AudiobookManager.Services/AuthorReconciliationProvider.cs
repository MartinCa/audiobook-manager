using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// See <see cref="IAuthorReconciliationProvider"/>. Mirrors
/// <see cref="SeriesReconciliationProvider"/>'s matching and classification, minus the
/// ignored-list and part-mismatch sections a standalone book has no use for.
/// </summary>
public class AuthorReconciliationProvider : IAuthorReconciliationProvider
{
    /// <summary>Same rationale as <see cref="SeriesReconciliationProvider.MaxReconciliationRosterEntries"/>, scoped to one author's standalone bibliography.</summary>
    public const int MaxReconciliationRosterEntries = 5_000;

    /// <summary>Same rationale as <see cref="SeriesReconciliationProvider.MaxReconciliationOwnedKeys"/>, scoped to one author's standalone owned books.</summary>
    public const int MaxReconciliationOwnedKeys = 20_000;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPersonRepository _personRepository;

    public AuthorReconciliationProvider(IAudiobookRepository audiobookRepository, IPersonRepository personRepository)
    {
        _audiobookRepository = audiobookRepository;
        _personRepository = personRepository;
    }

    public async Task<AuthorReconciliation> GetReconciliationAsync(long personId)
    {
        var (person, rosterOverflow) = await _personRepository.GetByIdWithExpectedBooksBoundedAsync(
            personId, MaxReconciliationRosterEntries);
        if (rosterOverflow)
        {
            throw new InvalidOperationException(
                $"Author {personId} has at least {MaxReconciliationRosterEntries + 1} standalone-book roster entries, exceeding the {MaxReconciliationRosterEntries} the detail view reconciles.");
        }

        var expected = person?.ExpectedBooks ?? new List<AuthorExpectedBook>();
        var active = expected.Where(e => !e.IsIgnored).ToList();
        var ignored = expected
            .Where(e => e.IsIgnored)
            .Select(ToExpectedInfo)
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var (ownedKeys, ownedOverflow) = await _audiobookRepository.GetStandaloneOwnedKeysByAuthorAsync(
            personId, MaxReconciliationOwnedKeys);
        if (ownedOverflow)
        {
            throw new InvalidOperationException(
                $"Author {personId} has at least {MaxReconciliationOwnedKeys + 1} owned standalone books, exceeding the {MaxReconciliationOwnedKeys} the detail view reconciles.");
        }

        var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(ownedKeys);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var unmatched = active
            .Where(e => !ownedIndex.Contains(SeriesRosterMatcher.BookKey.From(null, e.Title)))
            .ToList();

        var missing = unmatched
            .Where(e => !ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today))
            .Select(ToExpectedInfo)
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        var upcoming = unmatched
            .Where(e => ExpectedBookClassifier.IsUpcoming(e.ReleaseDate, e.Year, today))
            .Select(ToExpectedInfo)
            .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id)
            .ToList();

        return new AuthorReconciliation(missing, upcoming, ignored, ExpectedBookCount: active.Count, OwnedCount: ownedKeys.Count);
    }

    /// <inheritdoc cref="IAuthorReconciliationProvider.GetBulkMissingOrUpcomingAuthorIdsAsync"/>
    public async Task<(HashSet<long> HasMissingBooks, HashSet<long> HasUpcomingBooks)> GetBulkMissingOrUpcomingAuthorIdsAsync()
    {
        var activeExpected = await _personRepository.GetAllActiveAuthorExpectedBooksAsync();
        var hasMissing = new HashSet<long>();
        var hasUpcoming = new HashSet<long>();
        if (activeExpected.Count == 0)
        {
            return (hasMissing, hasUpcoming);
        }

        var personIds = activeExpected.Select(e => e.PersonId).Distinct().ToList();
        var ownedTitlesByAuthor = await _audiobookRepository.GetStandaloneOwnedTitlesByAuthorsAsync(personIds);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var group in activeExpected.GroupBy(e => e.PersonId))
        {
            // Same matching (SeriesRosterMatcher, title-only - a standalone book has no
            // position) and Missing-vs-Upcoming classification (ExpectedBookClassifier) as
            // GetReconciliationAsync, just batched across every author with a roster entry
            // instead of one author at a time. GetReconciliationAsync enforces
            // MaxReconciliationRosterEntries/MaxReconciliationOwnedKeys per author and throws on
            // overflow - the right response for a single detail-page request. This is a
            // list-filter endpoint that classifies every author with a roster in one pass, so
            // throwing here would take the whole authors list down over one pathological author.
            // Instead, an author past either cap is left out of both result sets (silently
            // unclassifiable to the filter, exactly like the detail view refuses to reconcile it)
            // rather than letting an unbounded roster or owned-title set through uncapped.
            var ownedTitles = ownedTitlesByAuthor.GetValueOrDefault(group.Key, new List<string>());
            if (group.Count() > MaxReconciliationRosterEntries || ownedTitles.Count > MaxReconciliationOwnedKeys)
            {
                continue;
            }

            var ownedIndex = new SeriesRosterMatcher.OwnedBookIndex(
                ownedTitles.Select(t => new Database.Repositories.SeriesOwnedKey(0, null, t)));

            foreach (var expected in group)
            {
                if (ownedIndex.Contains(SeriesRosterMatcher.BookKey.From(null, expected.Title)))
                {
                    continue;
                }

                if (ExpectedBookClassifier.IsUpcoming(expected.ReleaseDate, expected.Year, today))
                {
                    hasUpcoming.Add(group.Key);
                }
                else
                {
                    hasMissing.Add(group.Key);
                }
            }
        }

        return (hasMissing, hasUpcoming);
    }

    private static AuthorExpectedBookInfo ToExpectedInfo(AuthorExpectedBook book) => new()
    {
        Id = book.Id,
        Title = book.Title,
        Year = book.Year,
        ReleaseDate = book.ReleaseDate,
        SourceUrl = book.SourceUrl,
        IsIgnored = book.IsIgnored,
    };
}
