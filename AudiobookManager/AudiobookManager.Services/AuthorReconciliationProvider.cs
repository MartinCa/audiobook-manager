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

        return new AuthorReconciliation(missing, upcoming, ExpectedBookCount: active.Count, OwnedCount: ownedKeys.Count);
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
