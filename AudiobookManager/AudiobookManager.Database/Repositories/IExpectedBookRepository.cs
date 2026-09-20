using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public interface IExpectedBookRepository
{
    /// <summary>
    /// The expected books linked to <paramref name="personId"/>, ordered by id and capped at
    /// <paramref name="maxBooks"/> + 1 rows. The overflow flag is the caller's signal that the
    /// list was larger than the cap - a bounded read, never a whole-table transfer.
    /// </summary>
    Task<(List<ExpectedBook> Items, bool Overflow)> GetByAuthorBoundedAsync(long personId, int maxBooks);

    /// <summary>
    /// The expected books in the catalog series <paramref name="seriesId"/>, ordered by id and
    /// capped at <paramref name="maxBooks"/> + 1 rows (see <see cref="GetByAuthorBoundedAsync"/>
    /// for the overflow contract).
    /// </summary>
    Task<(List<ExpectedBook> Items, bool Overflow)> GetBySeriesBoundedAsync(long seriesId, int maxBooks);

    /// <summary>
    /// The non-ignored expected books joined to their author links, reduced to
    /// <see cref="ExpectedBookAuthorBookRef"/> and capped at <paramref name="maxRefs"/> + 1 rows -
    /// the batched classifier input for the bulk author-list filter. The overflow flag reports
    /// whether the library outgrew the cap (a bounded read, never a whole-table transfer);
    /// rows are ordered by expected book id. Accent-folding/ordering are deliberately not applied
    /// here; it is a bulk-read input, not a user-facing list. The series-name resolution pass
    /// is chunked internally (see <c>ExpectedBookRepository.MaxInClauseIdsPerQuery</c>) so a
    /// large ref set never translates into a single over-limit SQLite <c>IN</c> clause.
    /// </summary>
    Task<(List<ExpectedBookAuthorBookRef> Items, bool Overflow)> GetActiveAuthorBookRefsAsync(int maxRefs);

    /// <summary>
    /// Inserts an expected book, or refreshes the existing row for the same
    /// (<see cref="ExpectedBookUpsert.SourceName"/>, <see cref="ExpectedBookUpsert.SourceBookId"/>)
    /// pair. Title/year/release date/source URL are always overwritten (a source routinely ships
    /// a placeholder title or provisional date and corrects it on a later poll);
    /// <see cref="ExpectedBookUpsert.ImageUrl"/> only overwrites when the new value is non-null,
    /// and <see cref="ExpectedBookUpsert.IsCompilation"/> only when the poll actually carries a
    /// value (an author-shaped poll passes null and preserves whatever a series-shaped poll
    /// established - see <c>ExpectedBookRepository.UpsertAsync</c>). The series fields are
    /// overwritten only by a poll that carries a series identity
    /// (<see cref="ExpectedBookUpsert.SeriesId"/>/<see cref="ExpectedBookUpsert.SourceSeriesId"/>
    /// set) - an author-shaped poll never clears the series placement another scope established.
    /// Author links are added idempotently and a name-only link is upgraded when the poll
    /// resolves its person. When the caller knows a real <see cref="ExpectedBookUpsert.SourceBookId"/>
    /// and no row carries it yet, a row whose <see cref="ExpectedBook.SourceBookId"/> is a
    /// synthetic legacy id (or null - an id-less poll) and whose natural key matches is adopted
    /// in place - its <see cref="ExpectedBook.SourceBookId"/> is overwritten and its data
    /// refreshed - rather than a duplicate row being created; a row whose source id is real is
    /// never adopted by natural key. Rows are never deleted by this method, and the user's
    /// <see cref="ExpectedBook.IsIgnored"/> flag is never reset. Returns the id of the stored
    /// (inserted or refreshed) row.
    /// </summary>
    Task<long> UpsertAsync(ExpectedBookUpsert upsert);

    /// <summary>
    /// Batch form of <see cref="UpsertAsync"/> for a whole roster replace: one id per input in
    /// input order, each being the stored row's id, so the caller can hand the ids back as the
    /// keep-list for <see cref="UnlinkSeriesBooksAsync"/>.
    /// </summary>
    Task<List<long>> UpsertManyAsync(IReadOnlyList<ExpectedBookUpsert> upserts);

    /// <summary>
    /// Removes <paramref name="personId"/>'s author links on any book not in
    /// <paramref name="keepBookIds"/> (all of the person's links when the keep-list is empty).
    /// The books themselves are left alone - orphan cleanup is
    /// <see cref="DeleteOrphanExpectedBooksAsync"/>'s separate, explicit job.
    /// </summary>
    Task PruneAuthorLinksAsync(long personId, IReadOnlyList<long> keepBookIds);

    /// <summary>
    /// Drops the catalog <see cref="ExpectedBook.SeriesId"/> on the series' books not in
    /// <paramref name="keepBookIds"/> (all of them when the keep-list is empty), so a series that
    /// no longer reports a book stops listing it while the row - and any author links it carries -
    /// survives. The source-series fields are deliberately kept: they record what the source
    /// reported, which a later refresh either overwrites or not.
    /// </summary>
    Task UnlinkSeriesBooksAsync(long seriesId, IReadOnlyList<long> keepBookIds);

    /// <summary>
    /// Deletes expected books that are no longer reachable from anything: no author links and no
    /// series link (the natural end-state of <see cref="PruneAuthorLinksAsync"/> removing the last
    /// link on a standalone book). A series-linked or other-author-linked book is kept.
    /// </summary>
    Task DeleteOrphanExpectedBooksAsync();

    /// <summary>
    /// Sets the user's ignore flag on one expected book. An ignored book stays stored; it is
    /// simply hidden from the missing/upcoming views.
    /// </summary>
    Task SetIgnoredAsync(long expectedBookId, bool ignored);

    /// <summary>
    /// Sets the user's ignore flag on one expected book addressed by its stable
    /// <see cref="ExpectedBook.Id"/> - the unambiguous addressing path for roster-derived
    /// dismissals (ignore/unignore and the upcoming-view dismissal), since a title or series
    /// position can name two rows. No-op success when the id does not exist; callers that need
    /// to report a missing entry read <see cref="GetByIdAsync"/> first.
    /// </summary>
    Task SetIgnoredByIdAsync(long expectedBookId, bool ignored);

    /// <summary>
    /// Sets the user's ignore flag on the expected book linked to <paramref name="personId"/>
    /// whose title matches (trimmed, case-insensitive) - the author-roster counterpart of
    /// <see cref="ISeriesRepository.SetExpectedBookIgnoredAsync"/>, targeting the same shared
    /// <see cref="ExpectedBook"/> row the author reconciliation reads, so a dismissal hides the
    /// book from every scope (author, series, other authors) at once. Throws
    /// <see cref="KeyNotFoundException"/> when no such book exists for the author.
    ///
    /// Kept as the compatibility fallback for the title-addressed API only - new callers address
    /// the row by its stable id via <see cref="SetIgnoredByIdAsync"/> because a person can carry
    /// two roster entries with the same title. Returns the affected row's
    /// <see cref="ExpectedBook.SeriesId"/> (null for a standalone book) so the caller can
    /// invalidate the series-facing reconciliation cache, whose view of a shared row changes
    /// with its ignore flag.
    ///
    /// The roster read is bounded to <paramref name="maxBooks"/> + 1 rows like the sibling
    /// bounded reads (see <see cref="GetByAuthorBoundedAsync"/>), and the flag write is a
    /// set-based <c>ExecuteUpdateAsync</c> (see <see cref="SetIgnoredAsync"/>) - never a tracked
    /// read-modify-write, so a concurrent prune/orphan-delete cannot race this write into a
    /// <c>DbUpdateConcurrencyException</c>. When the roster outgrows the cap and the natural key
    /// is not resolvable within the readable prefix, the method degrades to the same not-found
    /// error rather than reading the unbounded remainder (or guessing at a row).
    /// </summary>
    Task<long?> SetExpectedBookIgnoredByPersonAsync(long personId, string title, bool ignored, int maxBooks);

    /// <summary>The expected book and its author links, or null when the id does not exist.</summary>
    Task<ExpectedBook?> GetByIdAsync(long id);

    /// <summary>
    /// The expected book carrying this exact (source name, source book id) pair - the unified
    /// row's dedup identity (see <see cref="UpsertAsync"/>) - or null. Read-side lookup for the
    /// source-identity dismissal route; the write itself goes through
    /// <see cref="SetIgnoredByIdAsync"/>.
    /// </summary>
    Task<ExpectedBook?> GetBySourceAsync(string sourceName, string sourceBookId);
}