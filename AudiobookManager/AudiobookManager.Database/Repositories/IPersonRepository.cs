using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IPersonRepository
{
    Task<Person> GetOrCreatePerson(string name);

    /// <summary>
    /// The tracked person rows for the names in <paramref name="names"/>, resolved in one batched
    /// <c>WHERE name IN (...)</c> query - the batch equivalent of the single-name lookup, for
    /// resolving a whole roster's distinct source author names without one round trip per name.
    /// Names that have no <see cref="Person"/> row are simply absent from the result map.
    /// Read-only - never creates a row. Exact-match only, and deliberately NOT accent-folded:
    /// <c>persons.name</c> is unique (the unique index), so each name either resolves to its one
    /// row or is absent, and the caller is matching the source's exact spelling.
    /// </summary>
    Task<Dictionary<string, Person>> GetByNamesAsync(IReadOnlyCollection<string> names);

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
    /// Sets or clears (when <paramref name="sourceId"/> is null) this person's author match.
    /// Throws <see cref="KeyNotFoundException"/> when no person with this id exists.
    /// </summary>
    Task SetAuthorMatchAsync(long personId, string? matchedSourceName, string? sourceId, string? sourceUrl);

    /// <summary>Stamps when an author's roster was last refreshed from its matched source.</summary>
    Task SetLastRefreshedAtAsync(long personId, DateTime at);

    /// <summary>Every author with a Hardcover match, for the bulk "refresh all matched authors" sweep.</summary>
    Task<List<Person>> GetMatchedAuthorsAsync();

    /// <summary>
    /// Carries everything the author <see cref="Person"/> named <paramref name="fromName"/> owns
    /// beyond its books - the source match (kept only when the destination has none), the follow,
    /// the unified-roster links and upcoming releases - over to <paramref name="toName"/>, then
    /// removes <paramref name="fromName"/> when no book references it any more (as author or
    /// narrator). The last step of renaming an author: the books themselves are rewritten
    /// through <c>AudiobookService.UpdateAudiobook</c> first (which find-or-creates the
    /// destination by name), so by the time this runs the source holds no authored books, and a
    /// failed book rewrite never gets here.
    ///
    /// The destination is created when it does not exist yet (an author with no books at all is
    /// instead renamed in place, keeping its id). Returns false when nothing was done: the names
    /// are equal or no person is named <paramref name="fromName"/>.
    /// </summary>
    Task<bool> MergeAuthorAsync(string fromName, string toName);
}
