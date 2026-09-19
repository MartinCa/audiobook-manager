using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IPersonRepository
{
    Task<Person> GetOrCreatePerson(string name);

    /// <summary>
    /// Batch equivalent of <see cref="GetOrCreatePerson"/>: resolves every distinct name in
    /// <paramref name="names"/> in a single query, creates whichever ones don't already exist
    /// in a single insert, and returns one <see cref="Person"/> per input name (duplicates in
    /// the input collapse to the same instance).
    /// </summary>
    Task<Dictionary<string, Person>> GetOrCreatePersons(IEnumerable<string> names);
    /// <summary>
    /// An author whose folded name equals the input value's folded name - the "this value already
    /// exists" answer for the entry-status classification. Identical to what the accent-insensitive
    /// AND the case-insensitive search does (folded-column equality, SQLite LIKE semantics), so a
    /// typed "rené" matches a stored "René" and "brandon sanderson" matches "Brandon Sanderson".
    /// </summary>
    Task<AuthorSummaryRow?> FindAuthorByFoldedNameAsync(string value);

    /// <summary>
    /// A narrator whose folded name equals the input value's folded name - the narrator
    /// counterpart of <see cref="FindAuthorByFoldedNameAsync"/>, scoped to persons that actually
    /// narrate books. Backs the narrator entry-status classification in the edit form.
    /// </summary>
    Task<AuthorSummaryRow?> FindNarratorByFoldedNameAsync(string value);

    /// <summary>
    /// The distinct author names the entry-status classification scores as "similar" candidates -
    /// a bounded (capped), deliberately permissive prefilter (full-query containment ranked ahead
    /// of first-token containment) over which the fuzzy "similar" decision is made. Returns id +
    /// name so the classification can offer the match for the author link.
    /// </summary>
    Task<List<AuthorSummaryRow>> SearchAuthorNamesAsync(string query, int limit);

    /// <summary>
    /// The narrator counterpart of <see cref="SearchAuthorNamesAsync"/>: bounded narrator-name
    /// candidates for the narrator entry-status classification.
    /// </summary>
    Task<List<AuthorSummaryRow>> SearchNarratorNamesAsync(string query, int limit);

    /// <summary>
    /// Distinct names of authors that have at least one book. This is the similar-author
    /// detection's input, not a response: the whole set is what the grouping compares, and it
    /// stays behind SimilarValueService's bounded+cached computation. The entry-time autocomplete
    /// it once also backed is <see cref="SearchAuthorNamesAsync"/>, which is bounded.
    /// </summary>
    Task<List<string>> GetAuthorNamesAsync();

    /// <summary>Every author that has at least one book, with its book count projected in SQL.</summary>
    Task<List<AuthorSummaryRow>> GetAllAuthorSummariesAsync();

    /// <summary>
    /// One page of all authors, ordered by name (then book count, then id) with the total, for the
    /// paged authors browse list. <paramref name="search"/> folds accents and filters on the
    /// precomputed <c>NameFolded</c> column; a null or blank search disables the filter.
    ///
    /// <paramref name="filter"/> layers the additional followed/book-count/matched/refreshed
    /// filters from <see cref="AuthorSummaryFilter"/> on top; <paramref name="restrictToIds"/> is
    /// how the caller applies that filter's missing/upcoming-book fields, which this repository
    /// cannot evaluate itself (see <see cref="AuthorSummaryFilter"/>'s doc) - when non-null, only
    /// authors whose id is in this set are returned.
    /// </summary>
    Task<(List<AuthorSummaryRow> Items, int Total)> GetAuthorSummariesPagedAsync(
        string? search, int limit, int offset,
        AuthorSummaryFilter? filter = null, IReadOnlyCollection<long>? restrictToIds = null,
        IReadOnlyCollection<long>? excludeIds = null);

    /// <summary>
    /// Every non-ignored standalone-book roster entry in the library, as (person id, title, year,
    /// release date), for the authors list filter's bulk missing/upcoming-book reconciliation
    /// (<see cref="AuthorReconciliationProvider.GetBulkMissingOrUpcomingAuthorIdsAsync"/>). Only
    /// runs when that filter is actually requested.
    /// </summary>
    Task<List<AuthorExpectedBookRef>> GetAllActiveAuthorExpectedBooksAsync();

    /// <summary>Name-matching authors, with the book count projected in SQL, paged with a total.</summary>
    Task<(List<AuthorSummaryRow> Items, int Total)> SearchAuthorSummariesAsync(string query, int limit, int offset);

    /// <summary>A single author's id/name/book-count, or null when the author does not exist.</summary>
    Task<AuthorSummaryRow?> GetAuthorSummaryAsync(long authorId);

    /// <summary>
    /// How many books carry each of only the given author names, for the similar-author detection:
    /// the detection shows a book count per candidate, and the page only needs counts for the
    /// candidates it actually returns. Loads no per-book rows - just a GROUP BY over the
    /// book/author link.
    /// </summary>
    Task<Dictionary<string, int>> GetAuthorBookCountsAsync(IReadOnlyCollection<string> authorNames);

    /// <summary>The tracked person row by id, or null - for the Hardcover-match write path.</summary>
    Task<Person?> GetByIdAsync(long id);

    /// <summary>
    /// Sets or clears (when <paramref name="sourceId"/> is null) this person's Hardcover author
    /// match. Throws <see cref="KeyNotFoundException"/> when no person with this id exists.
    /// </summary>
    Task SetHardcoverMatchAsync(long personId, string? sourceId, string? sourceName, string? sourceUrl);

    /// <summary>
    /// The tracked person row plus its standalone-books roster, bounded to
    /// <paramref name="maxExpectedBooks"/> + 1 rows - the author-roster counterpart of
    /// <c>ISeriesRepository.GetByNameWithExpectedBooksBoundedAsync</c>.
    /// </summary>
    Task<(Person? Person, bool Overflow)> GetByIdWithExpectedBooksBoundedAsync(long id, int maxExpectedBooks);

    /// <summary>Replaces an author's whole standalone-books roster, tolerating a re-refresh's read-then-replace pattern.</summary>
    Task ReplaceAuthorExpectedBooksAsync(long personId, List<AuthorExpectedBook> expectedBooks);

    /// <summary>Stamps when an author's standalone-books roster was last refreshed from its matched source.</summary>
    Task SetLastRefreshedAtAsync(long personId, DateTime at);

    /// <summary>Every author with a Hardcover match, for the bulk "refresh all matched authors" sweep.</summary>
    Task<List<Person>> GetMatchedAuthorsAsync();

    /// <summary>
    /// Sets the ignore flag on a standalone-book roster entry addressed by title. Throws
    /// <see cref="KeyNotFoundException"/> when no entry with that title exists for the author.
    /// </summary>
    Task SetAuthorExpectedBookIgnoredAsync(long personId, string title, bool ignored);
}
