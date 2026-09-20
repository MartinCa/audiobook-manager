using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AudiobookManager.Database.Repositories;

public class ExpectedBookRepository : IExpectedBookRepository
{
    /// <summary>
    /// The largest id list one SQLite <c>IN</c> clause carries when the repository resolves
    /// series links for a bounded read's result set. The dedicated cap (e.g. the 100k bulk
    /// author-filter refs) can exceed SQLite's compiled variable limit by orders of magnitude,
    /// so the follow-up lookups chunk to this size instead of risking a single over-limit query.
    /// </summary>
    public const int MaxInClauseIdsPerQuery = 500;

    private readonly DatabaseContext _db;

    public ExpectedBookRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<(List<ExpectedBook> Items, bool Overflow)> GetByAuthorBoundedAsync(long personId, int maxBooks)
    {
        var books = await _db.ExpectedBooks
            .AsNoTracking()
            .Where(b => b.AuthorLinks.Any(l => l.PersonId == personId))
            .Include(b => b.AuthorLinks)
            .Include(b => b.Series)
            .AsSplitQuery()
            .OrderBy(b => b.Id)
            .Take(maxBooks + 1)
            .ToListAsync();

        return books.Count > maxBooks
            ? (books.Take(maxBooks).ToList(), true)
            : (books, false);
    }

    public async Task<(List<ExpectedBook> Items, bool Overflow)> GetBySeriesBoundedAsync(long seriesId, int maxBooks)
    {
        var books = await _db.ExpectedBooks
            .AsNoTracking()
            .Where(b => b.SeriesId == seriesId)
            .Include(b => b.AuthorLinks)
            .Include(b => b.Series)
            .AsSplitQuery()
            .OrderBy(b => b.Id)
            .Take(maxBooks + 1)
            .ToListAsync();

        return books.Count > maxBooks
            ? (books.Take(maxBooks).ToList(), true)
            : (books, false);
    }

    public async Task<(List<ExpectedBookAuthorBookRef> Items, bool Overflow)> GetActiveAuthorBookRefsAsync(int maxRefs)
    {
        // First stage: the refs themselves, capped at maxRefs + 1.
        var refs = await _db.ExpectedBookAuthors
            .AsNoTracking()
            .Where(l => l.PersonId != null && !l.ExpectedBook.IsIgnored)
            .OrderBy(l => l.ExpectedBookId)
            .ThenBy(l => l.PersonId)
            .Select(l => new ExpectedBookAuthorBookRef(
                l.PersonId!.Value, l.ExpectedBookId, l.ExpectedBook.Title,
                l.ExpectedBook.Year, l.ExpectedBook.ReleaseDate,
                null, l.ExpectedBook.SourceSeriesId, l.ExpectedBook.SeriesPosition))
            .Take(maxRefs + 1)
            .ToListAsync();

        // The local series name a ref's classifier needs when the book is linked to a catalog
        // series cannot be projected through the series navigation (EF refuses the nullable `?.`
        // in a select), so it is resolved by a second, equally-bounded read over the refs' own
        // expected-book ids - never a whole-table transfer. Every read is capped at the same refs
        // limit plus one.
        var seriesNameByBookId = new Dictionary<long, string?>();
        if (refs.Count > 0)
        {
            var bookIds = refs.Select(r => r.ExpectedBookId).Distinct().ToList();
            // NEVER one IN clause over the whole set: maxRefs is sized for a 100k-row library,
            // and SQLite's compiled variable limit (far smaller than that) would make a single
            // `IN (...)` query fail outright. The same bounded reads are chunked so each carries
            // a fixed-sized id list no matter how large the library got.
            var seriesIdRows = new List<(long Id, long? SeriesId)>();
            foreach (var chunk in bookIds.Chunk(MaxInClauseIdsPerQuery))
            {
                var chunkRows = await _db.ExpectedBooks
                    .AsNoTracking()
                    .Where(b => chunk.Contains(b.Id))
                    .Select(b => new { b.Id, b.SeriesId })
                    .ToListAsync();
                seriesIdRows.AddRange(chunkRows.Select(r => (r.Id, r.SeriesId)));
            }

            var distinctSeriesIds = seriesIdRows
                .Select(r => r.SeriesId)
                .Where(id => id is not null)
                .Distinct()
                .ToList();
            var seriesNamesById = new Dictionary<long, string>();
            foreach (var chunk in distinctSeriesIds.Cast<long>().Chunk(MaxInClauseIdsPerQuery))
            {
                var seriesRows = await _db.Series
                    .AsNoTracking()
                    .Where(s => chunk.Contains(s.Id))
                    .Select(s => new { s.Id, s.Name })
                    .ToListAsync();
                foreach (var row in seriesRows)
                {
                    seriesNamesById[row.Id] = row.Name;
                }
            }

            foreach (var idRow in seriesIdRows)
            {
                seriesNameByBookId[idRow.Id] = idRow.SeriesId is long seriesId
                    ? seriesNamesById.GetValueOrDefault(seriesId)
                    : null;
            }
        }

        var withSeriesNames = new List<ExpectedBookAuthorBookRef>(refs.Count);
        foreach (var bookRef in refs)
        {
            withSeriesNames.Add(new ExpectedBookAuthorBookRef(
                bookRef.PersonId, bookRef.ExpectedBookId, bookRef.Title, bookRef.Year, bookRef.ReleaseDate,
                seriesNameByBookId.GetValueOrDefault(bookRef.ExpectedBookId),
                bookRef.SourceSeriesId, bookRef.SeriesPart));
        }

        return withSeriesNames.Count > maxRefs
            ? (withSeriesNames.Take(maxRefs).ToList(), true)
            : (withSeriesNames, false);
    }

    public async Task<long> UpsertAsync(ExpectedBookUpsert upsert)
    {
        var now = DateTime.UtcNow;

        // The dedup identity is (source_name, source_book_id); a caller that knows the real
        // source book id finds the row that already carries it.
        ExpectedBook? existing = null;
        if (!string.IsNullOrEmpty(upsert.SourceBookId))
        {
            existing = await FindBySourceKeyAsync(upsert.SourceName, upsert.SourceBookId);
        }

        // Absent an exact id match, locate the adoptable row this polled book corresponds to and
        // adopt it in place instead of inserting a parallel row. Two families adopt: rows copied
        // from the legacy roster tables (a synthetic 'legacy-author:'/'legacy-series:' id - see
        // the migration's hand-edited copy - or no id at all from an id-less poll), and rows whose
        // whole identity lives under a DIFFERENT source name (a series/author the user re-matched
        // from source A to source B): the natural-key match (same series link + title, or same
        // person link + title) is the evidence the two reports describe the same real-world book,
        // so the row's old source identity is superseded rather than the row being orphaned and a
        // fresh un-ignored copy inserted under the new source.
        if (existing is null)
        {
            existing = await FindAdoptableMatchAsync(upsert);
        }

        if (existing is not null)
        {
            // Overwrite a placeholder identity (synthetic or absent) with the polled book's real
            // source id, and - for a cross-source adoption - set BOTH SourceName and SourceBookId
            // from the poll: the row's old source identity is superseded, because the natural-key
            // match says this is the same real-world book under the new source. Never overwrite a
            // real id of the SAME source - the exact (source_name, source_book_id) lookup already
            // handles that identity, and the same-source natural key must not re-key a settled row.
            if (!string.IsNullOrEmpty(upsert.SourceBookId)
                && (!string.Equals(existing.SourceName, upsert.SourceName, StringComparison.Ordinal)
                    || existing.SourceBookId is null
                    || ExpectedBook.IsLegacySyntheticSourceBookId(existing.SourceBookId)))
            {
                existing.SourceName = upsert.SourceName;
                existing.SourceBookId = upsert.SourceBookId;
            }

            await ApplyRefreshAsync(existing, upsert, now);
            SyncAuthorLinks(existing, upsert.Authors);
            await _db.SaveChangesAsync();
            return existing.Id;
        }

        var created = new ExpectedBook
        {
            SourceName = upsert.SourceName,
            SourceBookId = upsert.SourceBookId,
            Title = upsert.Title,
            Year = upsert.Year,
            ReleaseDate = upsert.ReleaseDate,
            SourceUrl = upsert.SourceUrl,
            ImageUrl = upsert.ImageUrl,
            SeriesId = upsert.SeriesId,
            SourceSeriesId = upsert.SourceSeriesId,
            SourceSeriesName = upsert.SourceSeriesName,
            SeriesPosition = upsert.SeriesPosition,
            IsCompilation = upsert.IsCompilation ?? false,
            FirstSeenAt = now,
            LastRefreshedAt = now,
        };
        SyncAuthorLinks(created, upsert.Authors);
        _db.ExpectedBooks.Add(created);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Lost a race against a concurrent refresh discovering the same book (an author
            // bibliography refresh and a series roster refresh can report it around the same
            // time). Adopt the winner's row and re-apply this poll's data - the identical
            // read-then-insert adoption pattern as UpcomingReleaseRepository.UpsertAsync.
            // Unlike those single-entity graphs, the failed parent's author links were already
            // staged onto this context (SyncAuthorLinks ran before the insert, fixed up to
            // `created`). A detach of the parent that left them tracked would make the second
            // SaveChanges below re-insert them pointing at the never-inserted parent's id (0) -
            // either throwing on the foreign key or leaving orphaned link rows behind. The
            // current EF version cascade-detaches Added dependents when their Added parent is
            // detached, but the explicit detach below guarantees every child reachable from the
            // failed parent leaves the context with it instead of relying on that behavior.
            var stagedLinks = created.AuthorLinks.ToList();
            _db.Entry(created).State = EntityState.Detached;
            ChangeTrackerDetach.DetachTracked(
                _db.ChangeTracker.Entries<ExpectedBookAuthor>(),
                link => stagedLinks.Contains(link));

            if (string.IsNullOrEmpty(upsert.SourceBookId))
            {
                throw;
            }

            var winner = await FindBySourceKeyAsync(upsert.SourceName, upsert.SourceBookId);
            if (winner is null)
            {
                throw;
            }

            await ApplyRefreshAsync(winner, upsert, now);
            SyncAuthorLinks(winner, upsert.Authors);
            await _db.SaveChangesAsync();
            return winner.Id;
        }

        return created.Id;
    }

    public async Task<List<long>> UpsertManyAsync(IReadOnlyList<ExpectedBookUpsert> upserts)
    {
        // Sequential on purpose: each UpsertAsync runs its own read-then-insert against the
        // scope's single DatabaseContext, which EF forbids using from concurrent tasks. A roster
        // is a bounded source page (tens of rows at most), so the round trips are fine.
        var ids = new List<long>(upserts.Count);
        foreach (var upsert in upserts)
        {
            ids.Add(await UpsertAsync(upsert));
        }

        return ids;
    }

    private Task<ExpectedBook?> FindBySourceKeyAsync(string sourceName, string sourceBookId) =>
        _db.ExpectedBooks
            .Include(b => b.AuthorLinks)
            .FirstOrDefaultAsync(b => b.SourceName == sourceName && b.SourceBookId == sourceBookId);

    /// <summary>
    /// Locates the stored row this polled book corresponds to and that may therefore be adopted
    /// in place, or null. Two families of rows are adoptable, each matched by the same natural
    /// key:
    ///
    /// <list type="bullet">
    /// <item><b>Legacy rows</b> - a row whose <see cref="ExpectedBook.SourceBookId"/> is a
    /// synthetic <c>legacy-</c> id (the legacy-roster copy) or is still null (an id-less poll).
    /// These are the one-time migration-copy rows a first refresh adopts onto real source ids.</item>
    /// <item><b>Cross-source rows</b> - a row stored under a DIFFERENT
    /// <see cref="ExpectedBook.SourceName"/> with any real source id. A user can re-match a
    /// series or author from source A to source B; source B's polls then report the same
    /// real-world books under a new identity, and without this the source-A rows would be
    /// orphaned and deleted while fresh unrecognised copies (losing every dismiss/
    /// <see cref="ExpectedBook.IsIgnored"/> decision) were inserted.</item>
    /// </list>
    ///
    /// The natural key never guesses on title alone: a series match requires the same catalog
    /// series (<see cref="ExpectedBook.SeriesId"/> or <see cref="ExpectedBook.SourceSeriesId"/>)
    /// plus the same normalized title, and - when both sides carry one - the same
    /// <see cref="ExpectedBook.SeriesPosition"/>; an author match requires a link to one of the
    /// polled book's resolved <see cref="ExpectedBookAuthor.PersonId"/>s plus the same normalized
    /// title. No series key and no shared person link means no adoption.
    ///
    /// When the poll carries a source-series identity the series branch runs first; if it finds
    /// no candidate, the method still falls through to the author branch. A pre-migration
    /// dismissed STANDALONE legacy row (copied from <c>author_expected_books</c>, which had no
    /// series concept) carries no series fields for the series branch to match, yet the same book
    /// can be reported with a series identity by the author's bibliography feed. Without the
    /// fall-through that row is never adopted: a parallel row is inserted, the stale copy's
    /// author link is pruned, and the orphaned copy - and the user's ignore decision with it -
    /// is deleted, resurrecting the dismissed book as a fresh un-ignored row.
    /// </summary>
    private async Task<ExpectedBook?> FindAdoptableMatchAsync(ExpectedBookUpsert upsert)
    {
        IQueryable<ExpectedBook> legacyQuery = _db.ExpectedBooks
            .Include(b => b.AuthorLinks)
            // Single-arg StartsWith so EF translates it to LIKE 'prefix%' (the otherwise-idiomatic
            // two-arg comparisonType overload has no SQL translation). The prefixes contain no
            // LIKE wildcards, so no escaping is needed.
            .Where(b => b.SourceBookId == null
                || b.SourceBookId.StartsWith(ExpectedBook.LegacyAuthorSyntheticPrefix)
                || b.SourceBookId.StartsWith(ExpectedBook.LegacySeriesSyntheticPrefix));

        var legacyMatch = await FindNaturalKeyMatchAsync(upsert, legacyQuery);
        if (legacyMatch is not null)
        {
            return legacyMatch;
        }

        // Cross-source: any row stored under a different source name is a candidate, whatever its
        // real source id. The row's old identity is superseded on adoption (see UpsertAsync), so
        // the same real-world book is never split across two sources. Requires a polled real
        // source id: there is nothing to re-key a row to without one, so a null-id poll cannot
        // supersede another source's identity.
        if (string.IsNullOrEmpty(upsert.SourceBookId))
        {
            return null;
        }

        return await FindNaturalKeyMatchAsync(
            upsert,
            _db.ExpectedBooks
                .Include(b => b.AuthorLinks)
                .Where(b => b.SourceName != upsert.SourceName));
    }

    private async Task<ExpectedBook?> FindNaturalKeyMatchAsync(ExpectedBookUpsert upsert, IQueryable<ExpectedBook> candidateQuery)
    {
        if (upsert.SeriesId is not null || !string.IsNullOrEmpty(upsert.SourceSeriesId))
        {
            var seriesId = upsert.SeriesId;
            var sourceSeriesId = upsert.SourceSeriesId;
            IQueryable<ExpectedBook> seriesQuery = seriesId is not null && !string.IsNullOrEmpty(sourceSeriesId)
                ? candidateQuery.Where(b => b.SeriesId == seriesId || b.SourceSeriesId == sourceSeriesId)
                : seriesId is not null
                    ? candidateQuery.Where(b => b.SeriesId == seriesId)
                    : candidateQuery.Where(b => b.SourceSeriesId == sourceSeriesId);

            var title = upsert.Title;
            var position = upsert.SeriesPosition;
            var candidates = await seriesQuery.OrderBy(b => b.Id).ToListAsync();
            var seriesMatch = candidates.FirstOrDefault(b =>
                TitlesEqual(b.Title, title) && PositionsMatch(b.SeriesPosition, position));
            if (seriesMatch is not null)
            {
                return seriesMatch;
            }

            // Fall through to the author natural key - see the method doc for why the series
            // branch missing is not the end of the search.
        }

        var personIds = upsert.Authors.Select(a => a.PersonId).Where(p => p is not null).ToList();
        if (personIds.Count == 0)
        {
            return null;
        }

        var authorTitle = upsert.Title;
        var candidatesByAuthor = await candidateQuery
            .Where(b => b.AuthorLinks.Any(l => l.PersonId != null && personIds.Contains(l.PersonId)))
            .OrderBy(b => b.Id)
            .ToListAsync();

        return candidatesByAuthor.FirstOrDefault(b =>
            TitlesEqual(b.Title, authorTitle) && b.AuthorLinks.Any(l => l.PersonId != null && personIds.Contains(l.PersonId)));
    }

    /// <summary>
    /// Applies a freshly-polled book onto the stored row. Title/year/release date/source URL/
    /// compilation flag are always overwritten when the poll carries them - a source routinely
    /// ships an announced book under a placeholder title or a provisional date and corrects it
    /// once real details are available. <see cref="ExpectedBookUpsert.ImageUrl"/> is the one field
    /// that is legitimately nullable per poll (a transiently-missing cached_image), so it only
    /// overwrites when the new poll actually has one - a poll with no image must not wipe a
    /// previously stored one. FirstSeenAt is untouched (it is when the book was first seen, not
    /// last refreshed).
    ///
    /// <see cref="ExpectedBookUpsert.IsCompilation"/> is itself nullable: null means "this poll
    /// supplies no compilation data" (the author-shaped bibliography poll), which must preserve
    /// whatever a series-shaped poll established about the same row - an author refresh on a
    /// series-linked book must not flip its omnibus flag. Only a poll that actually says whether
    /// the book is a compilation overwrites the stored value.
    ///
    /// The series fields are only overwritten when this poll actually carries a series identity
    /// (<see cref="ExpectedBookUpsert.SeriesId"/> or <see cref="ExpectedBookUpsert.SourceSeriesId"/>
    /// set). The same book is discovered by an author refresh (which has no series identity at
    /// all) and by a series refresh, so an author-shaped poll must not clear the series placement
    /// the series-shaped one established. A series-bearing poll is authoritative for all four
    /// fields - it may legitimately move or rename the book within the series - and a series that
    /// stops reporting a book entirely is handled by <see cref="UnlinkSeriesBooksAsync"/>, not here.
    ///
    /// One guard on the series identity: a poll whose identity did not resolve to a local series
    /// (<see cref="ExpectedBookUpsert.SeriesId"/> null) does not clear an existing catalog link
    /// unless it PROVABLY names a different series in the SAME source namespace. The poll's
    /// <see cref="ExpectedBookUpsert.SourceSeriesId"/> is only comparable to the linked series'
    /// <c>MatchedSourceId</c> when that series is matched to the poll's own source: a series
    /// matched to a different source (or unmatched) makes the two ids incomparable, so the failed
    /// resolution is an artifact of THIS poll's lookup (the author-shaped refresh resolves source
    /// series by (source name, source series id), and a feed that reports the id under a
    /// different source name misses a series genuinely matched to that id), not evidence the book
    /// left the series. Without the guard the link would oscillate: this poll clears it, the
    /// series' next refresh re-links it. A same-source, genuinely-different id IS proof the book
    /// moved to another series within that source, and clears.
    /// </summary>
    private async Task ApplyRefreshAsync(ExpectedBook existing, ExpectedBookUpsert upsert, DateTime now)
    {
        var seriesId = upsert.SeriesId;
        if (seriesId is null && !string.IsNullOrEmpty(upsert.SourceSeriesId) && existing.SeriesId is not null)
        {
            // Load the linked catalog series' match identity - only now, and only the two columns
            // the guard compares, so the common paths (a resolved SeriesId, or an unlinked row)
            // never pay for it.
            var linked = await _db.Series
                .AsNoTracking()
                .Where(s => s.Id == existing.SeriesId)
                .Select(s => new { MatchedSourceName = s.MatchedSourceName, MatchedSourceId = s.MatchedSourceId })
                .FirstOrDefaultAsync();

            var sameSource = linked is not null
                && !string.IsNullOrEmpty(linked.MatchedSourceName)
                && linked.MatchedSourceId is not null
                && string.Equals(linked.MatchedSourceName, upsert.SourceName, StringComparison.Ordinal);
            var provablyDifferent = sameSource
                && !string.Equals(linked!.MatchedSourceId, upsert.SourceSeriesId, StringComparison.Ordinal);
            if (!provablyDifferent)
            {
                seriesId = existing.SeriesId;
            }
        }

        existing.Title = upsert.Title;
        existing.Year = upsert.Year;
        existing.ReleaseDate = upsert.ReleaseDate;
        existing.SourceUrl = upsert.SourceUrl;
        existing.ImageUrl = upsert.ImageUrl ?? existing.ImageUrl;
        if (upsert.SeriesId is not null || !string.IsNullOrEmpty(upsert.SourceSeriesId))
        {
            existing.SeriesId = seriesId;
            existing.SourceSeriesId = upsert.SourceSeriesId;
            existing.SourceSeriesName = upsert.SourceSeriesName;
            existing.SeriesPosition = upsert.SeriesPosition;
        }
        if (upsert.IsCompilation is bool isCompilation)
        {
            existing.IsCompilation = isCompilation;
        }
        existing.LastRefreshedAt = now;
    }

    /// <summary>
    /// Adds each polled author link that is not already present, matching by
    /// <see cref="ExpectedBookAuthor.PersonId"/> when both sides resolve one, otherwise by
    /// <see cref="ExpectedBookAuthor.AuthorName"/> (always stored, so a name-only poll still
    /// matches an existing person-linked row). When a poll resolves an author to a
    /// <see cref="ExpectedBookAuthor.PersonId"/> and the matching link is only an
    /// <see cref="ExpectedBookAuthor.AuthorName"/> (an id-less earlier poll or a poll whose name
    /// did not resolve at the time), the link is upgraded in place rather than a person-linked
    /// duplicate being added beside it. Never removes links: dropping a book's authors is
    /// <see cref="PruneAuthorLinksAsync"/>'s separate, explicit job.
    /// </summary>
    private static void SyncAuthorLinks(ExpectedBook book, IReadOnlyList<ExpectedBookAuthorLink> links)
    {
        foreach (var link in links)
        {
            var match = book.AuthorLinks.FirstOrDefault(l =>
                l.PersonId is not null && link.PersonId is not null && l.PersonId == link.PersonId);

            if (match is null)
            {
                match = book.AuthorLinks.FirstOrDefault(l =>
                    string.Equals(l.AuthorName, link.AuthorName, StringComparison.Ordinal));
            }

            if (match is null)
            {
                book.AuthorLinks.Add(new ExpectedBookAuthor
                {
                    ExpectedBookId = book.Id,
                    PersonId = link.PersonId,
                    AuthorName = link.AuthorName,
                });
            }
            else if (match.PersonId is null && link.PersonId is not null)
            {
                match.PersonId = link.PersonId;
            }
        }
    }

    public async Task PruneAuthorLinksAsync(long personId, IReadOnlyList<long> keepBookIds)
    {
        var allLinkIds = await _db.ExpectedBookAuthors
            .Where(l => l.PersonId == personId)
            .Select(l => l.Id)
            .ToListAsync();
        if (allLinkIds.Count == 0)
        {
            return;
        }

        // The keep-list is a caller-supplied set of expected-book ids that can exceed SQLite's
        // compiled-variable limit by orders of magnitude (a roster refresh hands over the whole
        // fetched bibliography), so the keep-match lookup is chunked like every other id-list
        // read in this class: each chunk's matching link ids are merged into one set, and every
        // link NOT in it is this author's to remove. Never one IN clause over the whole list.
        var keepLinkIds = new HashSet<long>();
        foreach (var chunk in keepBookIds.ToList().Chunk(MaxInClauseIdsPerQuery))
        {
            var matching = await _db.ExpectedBookAuthors
                .Where(l => l.PersonId == personId && chunk.Contains(l.ExpectedBookId))
                .Select(l => l.Id)
                .ToListAsync();
            keepLinkIds.UnionWith(matching);
        }

        var linkIds = allLinkIds.Where(id => !keepLinkIds.Contains(id)).ToList();
        if (linkIds.Count == 0)
        {
            return;
        }

        await _db.ExpectedBookAuthors.Where(l => linkIds.Contains(l.Id)).ExecuteDeleteAsync();

        // Set-based delete bypasses the change tracker - see DeleteOrphanExpectedBooksAsync.
        ChangeTrackerDetach.DetachTracked(_db.ChangeTracker.Entries<ExpectedBookAuthor>(), l => linkIds.Contains(l.Id));
    }

    public async Task UnlinkSeriesBooksAsync(long seriesId, IReadOnlyList<long> keepBookIds)
    {
        var allBookIds = await _db.ExpectedBooks
            .Where(b => b.SeriesId == seriesId)
            .Select(b => b.Id)
            .ToListAsync();
        if (allBookIds.Count == 0)
        {
            return;
        }

        // Same chunking rule as PruneAuthorLinksAsync: a keep-list larger than the chunk size
        // must not become one oversized IN clause heading for the update statement below.
        var keepBookIdSet = new HashSet<long>();
        foreach (var chunk in keepBookIds.ToList().Chunk(MaxInClauseIdsPerQuery))
        {
            var matching = await _db.ExpectedBooks
                .Where(b => b.SeriesId == seriesId && chunk.Contains(b.Id))
                .Select(b => b.Id)
                .ToListAsync();
            keepBookIdSet.UnionWith(matching);
        }

        var bookIds = allBookIds.Where(id => !keepBookIdSet.Contains(id)).ToList();
        if (bookIds.Count == 0)
        {
            return;
        }

        // Deliberately clears only the catalog link. The source-series fields (source_series_id,
        // source_series_name, series_position) are kept: they record what the source reported,
        // which a later refresh either overwrites or not, and dropping them would lose the reason
        // this book was ever attributed. Author-linked rows are never deleted here - an unlinked
        // book that still has authors stays discoverable through them.
        await _db.ExpectedBooks
            .Where(b => bookIds.Contains(b.Id))
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.SeriesId, (long?)null));

        // ExecuteUpdateAsync bypasses the change tracker; drop stale tracked rows (see delete
        // note below).
        ChangeTrackerDetach.DetachTracked(_db.ChangeTracker.Entries<ExpectedBook>(), b => bookIds.Contains(b.Id));
    }

    public async Task DeleteOrphanExpectedBooksAsync()
    {
        var orphanIds = await _db.ExpectedBooks
            .Where(b => b.SeriesId == null && !b.AuthorLinks.Any())
            .Select(b => b.Id)
            .ToListAsync();
        if (orphanIds.Count == 0)
        {
            return;
        }

        await _db.ExpectedBooks
            .Where(b => orphanIds.Contains(b.Id))
            .ExecuteDeleteAsync();

        // Set-based delete bypasses the change tracker: rows this context already loaded keep
        // pointing at deleted rowids, which SQLite is free to hand to a later insert, so the
        // ghost would shadow the new row. No collection a caller holds can contain an orphan:
        // Series.ExpectedBooks only includes series-linked rows (orphans have SeriesId = null),
        // and an orphan author link is removed with its book. Cascade rules remove the book's
        // remaining author links with it.
        ChangeTrackerDetach.DetachTracked(_db.ChangeTracker.Entries<ExpectedBook>(), b => orphanIds.Contains(b.Id));
        ChangeTrackerDetach.DetachTracked(_db.ChangeTracker.Entries<ExpectedBookAuthor>(), l => orphanIds.Contains(l.ExpectedBookId));
    }

    public async Task SetIgnoredAsync(long expectedBookId, bool ignored)
    {
        await _db.ExpectedBooks
            .Where(b => b.Id == expectedBookId)
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.IsIgnored, ignored));

        ChangeTrackerDetach.DetachTracked(_db.ChangeTracker.Entries<ExpectedBook>(), b => b.Id == expectedBookId);
    }

    /// <inheritdoc cref="IExpectedBookRepository.SetIgnoredByIdAsync"/>
    public Task SetIgnoredByIdAsync(long expectedBookId, bool ignored) => SetIgnoredAsync(expectedBookId, ignored);

    /// <summary>
    /// The author-roster counterpart of <see cref="SeriesRepository.SetExpectedBookIgnoredAsync"/>,
    /// addressing the SHARED expected-book row the author's reconciliation now reads from - the
    /// same row a series refresh (or another author) may link to, so a dismissal here hides the
    /// book from every scope at once. Located by the person link plus a trimmed,
    /// case-insensitive title match - the natural-key compatibility fallback kept for the
    /// title-addressed API; new callers should address the row by its stable
    /// <see cref="ExpectedBook.Id"/> via <see cref="SetIgnoredByIdAsync"/> instead, since a
    /// person can carry two roster entries with the same title.
    ///
    /// The roster read is bounded to <paramref name="maxBooks"/> + 1 rows exactly like the
    /// sibling reads (<see cref="GetByAuthorBoundedAsync"/>), and the ignore flag is written
    /// with a set-based <c>ExecuteUpdateAsync</c> (see <see cref="SetIgnoredAsync"/>) - never a
    /// tracked read-modify-write - so a concurrent refresh's prune/orphan-delete cannot throw a
    /// <c>DbUpdateConcurrencyException</c> at this write. A roster that outgrows the cap and does
    /// not resolve the natural key within the readable prefix degrades to "not found" rather
    /// than reading the unbounded remainder (or guessing at a row).
    /// </summary>
    public async Task<long?> SetExpectedBookIgnoredByPersonAsync(long personId, string title, bool ignored, int maxBooks)
    {
        var (books, overflow) = await GetByAuthorBoundedAsync(personId, maxBooks);

        var normalizedTitle = title.Trim();
        var book = books.FirstOrDefault(b => string.Equals(b.Title.Trim(), normalizedTitle, StringComparison.OrdinalIgnoreCase));
        if (book is null)
        {
            // Whether the roster overflowed the read cap or genuinely lacks the title, the row
            // is not resolvable within the bounded prefix - report the same not-found (the
            // overflow message just says why the search could not continue) rather than reading
            // the rest of the roster or mutating a row the natural key did not name.
            throw new KeyNotFoundException(overflow
                ? $"Expected book (title '{title}') not found for author {personId} within the first {maxBooks} roster entries"
                : $"Expected book (title '{title}') not found for author {personId}");
        }

        await _db.ExpectedBooks
            .Where(b => b.Id == book.Id)
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.IsIgnored, ignored));

        // ExecuteUpdateAsync bypasses the change tracker - a tracked stale copy would overwrite
        // the flag back on the next SaveChanges; see SetIgnoredAsync.
        ChangeTrackerDetach.DetachTracked(_db.ChangeTracker.Entries<ExpectedBook>(), b => b.Id == book.Id);

        // The series the caller's cache invalidation needs: a shared row's series view changes
        // with its ignore flag, so the service must drop that series' cached reconciliation.
        return book.SeriesId;
    }

    public Task<ExpectedBook?> GetByIdAsync(long id) =>
        _db.ExpectedBooks
            .AsNoTracking()
            .Include(b => b.AuthorLinks)
            .FirstOrDefaultAsync(b => b.Id == id);

    /// <inheritdoc cref="IExpectedBookRepository.GetBySourceAsync"/>
    public Task<ExpectedBook?> GetBySourceAsync(string sourceName, string sourceBookId) =>
        _db.ExpectedBooks
            .AsNoTracking()
            .Include(b => b.AuthorLinks)
            .FirstOrDefaultAsync(b => b.SourceName == sourceName && b.SourceBookId == sourceBookId);

    private static bool TitlesEqual(string? a, string? b) =>
        string.Equals(FoldedTitleKey(a), FoldedTitleKey(b), StringComparison.Ordinal);

    /// <summary>
    /// The accent-insensitive, case-insensitive title key used by natural-key adoption, folded via
    /// <see cref="AudiobookManager.Database.Search.AccentFolding.FoldPlain(string?)"/> - the same
    /// CLR folding the <c>fold_accents</c> SQL function applies to search - so "René" matches
    /// "Rene" in either direction. Kept in the Database layer deliberately: the adoption matching
    /// must not pull in the Services' name normalizers and close a Database -> Services cycle.
    /// </summary>
    private static string? FoldedTitleKey(string? title)
    {
        var folded = AccentFolding.FoldPlain(title?.Trim());
        return folded is null ? null : folded.ToLowerInvariant();
    }

    /// <summary>
    /// Positions are equal when both sides are non-null and equal. A missing position on either
    /// side does not disqualify a natural-key match - not every source report carries one.
    /// </summary>
    private static bool PositionsMatch(string? existing, string? polled) =>
        existing is null || polled is null || string.Equals(existing, polled, StringComparison.Ordinal);
}