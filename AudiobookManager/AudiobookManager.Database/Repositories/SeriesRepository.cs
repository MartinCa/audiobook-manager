using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class SeriesRepository : ISeriesRepository
{
    private readonly DatabaseContext _db;
    private readonly IExpectedBookRepository _expectedBookRepository;

    public SeriesRepository(DatabaseContext db, IExpectedBookRepository expectedBookRepository)
    {
        _db = db;
        _expectedBookRepository = expectedBookRepository;
    }

    /// <summary>
    /// Read-only projection source for the series overview. No tracking: the overview never
    /// mutates these, and tracking every series plus its full roster for the request's lifetime
    /// is pure change-detection overhead. Writers use the tracked
    /// <see cref="GetByNameWithExpectedBooksAsync"/> / <see cref="UpsertSeriesAsync"/> path.
    /// </summary>
    public async Task<List<Series>> GetAllWithExpectedBooksAsync()
    {
        return await _db.Series
            .AsNoTracking()
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .ToListAsync();
    }

    public async Task<Series?> GetByIdWithExpectedBooksAsync(long id)
    {
        return await _db.Series
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Series?> GetByNameWithExpectedBooksAsync(string name)
    {
        return await _db.Series
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Name == name);
    }

    /// <summary>
    /// The catalog row's metadata only, no roster: the series detail loads this on every page
    /// request and must not pull the expected-books collection along with it.
    /// </summary>
    public async Task<Series?> GetByNameAsync(string name)
    {
        return await _db.Series
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == name);
    }

    public Task<string?> GetNameByIdAsync(long id) =>
        _db.Series
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => s.Name)
            .FirstOrDefaultAsync();

    public async Task<Series?> GetByMatchedSourceIdAsync(string sourceName, string sourceId)
    {
        return await _db.Series
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.MatchedSourceName == sourceName && s.MatchedSourceId == sourceId);
    }

    /// <summary>
    /// The names of every matched series, names only - no roster. The library-wide
    /// series-part-mismatch sweep iterates the matched series to reconcile each one against its
    /// owned books; it must not load the full catalog (rosters included) to learn which series are
    /// matched.
    /// </summary>
    public async Task<List<string>> GetMatchedSeriesNamesAsync()
    {
        return await _db.Series
            .AsNoTracking()
            .Where(s => s.MatchedSourceName != null && s.MatchedSourceName != ""
                && s.MatchedSourceId != null && s.MatchedSourceId != "")
            .Select(s => s.Name)
            .ToListAsync();
    }

    /// <summary>
    /// The catalog row plus its roster, bounded to <paramref name="maxExpectedBooks"/> + 1 rows.
    /// The reconciliation's fuzzy matcher is explicitly capped, so the roster read must not
    /// materialize (or transfer) a pathological full roster just to learn it is too big: the
    /// query returns at most cap+1 entries and the caller is told whether the cap was breached.
    /// A roster at or under the cap comes back complete.
    /// </summary>
    public async Task<(Series? Series, bool Overflow)> GetByNameWithExpectedBooksBoundedAsync(
        string name, int maxExpectedBooks)
    {
        var row = await _db.Series
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == name);

        if (row is null)
        {
            return (null, false);
        }

        var books = await _db.ExpectedBooks
            .AsNoTracking()
            .Where(b => b.SeriesId == row.Id)
            .OrderBy(b => b.Id)
            .Take(maxExpectedBooks + 1)
            .ToListAsync();

        row.ExpectedBooks = books;
        return (row, books.Count > maxExpectedBooks);
    }

    public async Task<List<Series>> GetByNamesWithExpectedBooksAsync(List<string> names)
    {
        if (names.Count == 0)
        {
            return new List<Series>();
        }

        return await _db.Series
            .AsNoTracking()
            .Include(s => s.ExpectedBooks)
            .AsSplitQuery()
            .Where(s => names.Contains(s.Name))
            .ToListAsync();
    }

    /// <summary>
    /// Inserts the series if no row with the same <see cref="Series.Name"/> exists,
    /// otherwise updates the match metadata on the existing row.
    /// </summary>
    public async Task<Series> UpsertSeriesAsync(Series series)
    {
        var (row, _) = await UpsertByNameAsync(
            series.Name,
            row =>
            {
                row.MatchedSourceName = series.MatchedSourceName;
                row.MatchedSourceId = series.MatchedSourceId;
                row.MatchedSourceUrl = series.MatchedSourceUrl;
                row.MatchedSeriesName = series.MatchedSeriesName;
                row.MatchConfidence = series.MatchConfidence;
                row.LastRefreshedAt = series.LastRefreshedAt;
                row.IncludeOmnibusEditions = series.IncludeOmnibusEditions;
            },
            () => series);
        return row;
    }

    /// <summary>
    /// Insert-or-update keyed on the unique series name, tolerating the read-then-insert race.
    ///
    /// series.name is unique and this reads before it inserts, across an await on a
    /// request-scoped context - so two callers can both find the row missing and both try to
    /// create it. That happens for real: a bulk auto-match upserts a row per series while the
    /// user can be matching or toggling omnibus editions on one of those same series from the
    /// page, and the per-series endpoints share no lock with the bulk one. The loser used to
    /// fail the whole request with a raw "UNIQUE constraint failed: series.name" 500; it now
    /// adopts the winner's row and applies its own change on top, which is what the caller
    /// asked for either way.
    ///
    /// The bool reports whether THIS call inserted the row (false for a pre-existing row and
    /// for a winner adopted after losing the race) - the only caller that needs it uses it to
    /// decide whether a downstream failure warrants deleting the row it just created.
    /// </summary>
    private async Task<(Series Series, bool Created)> UpsertByNameAsync(string name, Action<Series> applyChanges, Func<Series> createNew)
    {
        var existing = await _db.Series.FirstOrDefaultAsync(s => s.Name == name);
        if (existing is not null)
        {
            applyChanges(existing);
            await _db.SaveChangesAsync();
            return (existing, false);
        }

        var inserted = createNew();
        _db.Series.Add(inserted);

        try
        {
            await _db.SaveChangesAsync();
            return (inserted, true);
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            _db.Entry(inserted).State = EntityState.Detached;

            var winner = await _db.Series.FirstOrDefaultAsync(s => s.Name == name);
            if (winner is null)
            {
                // Some other uniqueness constraint failed - not the race this handler is for.
                throw;
            }

            applyChanges(winner);
            await _db.SaveChangesAsync();
            return (winner, false);
        }
    }

    public async Task<ExpectedBook?> GetExpectedBookAsync(long id)
    {
        return await _db.ExpectedBooks.FindAsync(id);
    }

    /// <summary>
    /// Sets the ignore flag on a roster entry addressed by its natural key (series name plus
    /// position and/or title) - the series-scoped API's pre-unification addressing contract, kept
    /// as the compatibility surface: the client's missing/mismatch flows carry exactly the
    /// (position, title) pair the source reports, so this route needs no row id. The unified
    /// expected-book rows are refreshed IN PLACE across roster rewrites (their ids stay stable),
    /// which is precisely why the id-addressed dismissal routes
    /// (<see cref="ExpectedBookRepository.SetIgnoredByIdAsync"/>) are preferred for new callers.
    ///
    /// The roster read is bounded to <paramref name="maxBooks"/> + 1 rows like
    /// <see cref="GetByNameWithExpectedBooksBoundedAsync"/>, and the flag write is a set-based
    /// <c>ExecuteUpdateAsync</c> - never a tracked read-modify-write - so a concurrent
    /// <see cref="ExpectedBookRepository.UnlinkSeriesBooksAsync"/>/orphan delete cannot throw a
    /// <c>DbUpdateConcurrencyException</c> at this write. A roster that outgrows the cap and does
    /// not resolve the natural key within the readable prefix degrades to "not found" rather
    /// than reading the unbounded remainder (or guessing at a row).
    /// </summary>
    public async Task SetExpectedBookIgnoredAsync(string seriesName, string? position, string? title, bool ignored, int maxBooks)
    {
        var (series, overflow) = await GetByNameWithExpectedBooksBoundedAsync(seriesName, maxBooks);
        if (series is null)
        {
            throw new KeyNotFoundException($"Series '{seriesName}' not found");
        }

        // The bounded read's overflow probe is the (cap + 1)th row - not part of the readable
        // prefix, so it must not be matched against when the roster outgrows the cap.
        var books = overflow ? series.ExpectedBooks.Take(maxBooks).ToList() : series.ExpectedBooks;
        var book = MatchExpectedBook(books, position, title);
        if (book is null)
        {
            // Whether the roster overflowed the read cap or genuinely lacks the entry, the row
            // is not resolvable within the bounded prefix - report the same not-found (the
            // overflow message just says why the search could not continue) rather than reading
            // the rest of the roster or mutating a row the natural key did not name.
            throw new KeyNotFoundException(overflow
                ? $"Expected book (position '{position}', title '{title}') not found in series '{seriesName}' within the first {maxBooks} roster entries"
                : $"Expected book (position '{position}', title '{title}') not found in series '{seriesName}'");
        }

        await _db.ExpectedBooks
            .Where(b => b.Id == book.Id)
            .ExecuteUpdateAsync(b => b.SetProperty(x => x.IsIgnored, ignored));

        // ExecuteUpdateAsync bypasses the change tracker - a tracked stale copy would overwrite
        // the flag back on the next SaveChanges (see ExpectedBookRepository.SetIgnoredAsync).
        foreach (var entry in _db.ChangeTracker.Entries<ExpectedBook>().Where(e => e.Entity.Id == book.Id).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Read-side counterpart of <see cref="SetExpectedBookIgnoredAsync"/>'s entry lookup: the
    /// same natural-key rule, but null instead of an exception so callers can choose how to
    /// report a missing entry. Read-only - no tracking, the callers only read the result.
    /// </summary>
    public async Task<ExpectedBook?> FindExpectedBookAsync(string seriesName, string? position, string? title)
    {
        var books = await GetExpectedBooksAsync(seriesName);
        return books is null ? null : MatchExpectedBook(books, position, title);
    }

    /// <summary>
    /// The roster of one series, read-only, or null when no row with that name exists. Shared by
    /// the two read-side roster lookups so the identical series lookup + AsNoTracking fetch isn't
    /// duplicated - a null result is how the callers report a missing series, and an empty list
    /// (a matched series whose roster is empty) must stay distinct from it. Deliberately NOT used
    /// by <see cref="SetExpectedBookIgnoredAsync"/>, which locates its entry through the bounded
    /// <see cref="GetByNameWithExpectedBooksBoundedAsync"/> and writes the flag with a set-based
    /// <c>ExecuteUpdateAsync</c>.
    /// </summary>
    private async Task<List<ExpectedBook>?> GetExpectedBooksAsync(string seriesName)
    {
        var series = await _db.Series
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == seriesName);
        if (series is null)
        {
            return null;
        }

        return await _db.ExpectedBooks
            .AsNoTracking()
            .Where(b => b.SeriesId == series.Id)
            .ToListAsync();
    }

    /// <summary>
    /// The natural-key matching rule shared by every roster-entry lookup: trim + case-insensitive
    /// comparison, preferring an entry that matches both position and title, then either alone -
    /// a source may report a roster entry without a position at all.
    /// </summary>
    private static ExpectedBook? MatchExpectedBook(IEnumerable<ExpectedBook> books, string? position, string? title)
    {
        var hasPosition = !string.IsNullOrWhiteSpace(position);
        var hasTitle = !string.IsNullOrWhiteSpace(title);

        bool PositionMatches(ExpectedBook b) =>
            hasPosition && string.Equals(b.SeriesPosition?.Trim(), position!.Trim(), StringComparison.OrdinalIgnoreCase);

        bool TitleMatches(ExpectedBook b) =>
            hasTitle && string.Equals(b.Title.Trim(), title!.Trim(), StringComparison.OrdinalIgnoreCase);

        return books.FirstOrDefault(b => PositionMatches(b) && TitleMatches(b))
            ?? books.FirstOrDefault(PositionMatches)
            ?? books.FirstOrDefault(TitleMatches);
    }

    public async Task<ExpectedBook?> FindExpectedBookStrictAsync(string seriesName, string? position, string? title)
    {
        var books = await GetExpectedBooksAsync(seriesName);
        return books is null ? null : MatchExpectedBookStrict(books, position, title);
    }

    /// <summary>
    /// Strict variant: when both position and title are nonblank, a single row must match
    /// both — no fallback to either alone. When only one is supplied, that field alone is
    /// sufficient. This prevents the permissive fall-back from picking a wrong roster entry
    /// when the caller supplied both parts of the key but they map to different rows.
    /// </summary>
    private static ExpectedBook? MatchExpectedBookStrict(IEnumerable<ExpectedBook> books, string? position, string? title)
    {
        var hasPosition = !string.IsNullOrWhiteSpace(position);
        var hasTitle = !string.IsNullOrWhiteSpace(title);

        bool PositionMatches(ExpectedBook b) =>
            hasPosition && string.Equals(b.SeriesPosition?.Trim(), position!.Trim(), StringComparison.OrdinalIgnoreCase);

        bool TitleMatches(ExpectedBook b) =>
            hasTitle && string.Equals(b.Title.Trim(), title!.Trim(), StringComparison.OrdinalIgnoreCase);

        // When both parts are supplied, require both to match — no fallback.
        if (hasPosition && hasTitle)
        {
            return books.FirstOrDefault(b => PositionMatches(b) && TitleMatches(b));
        }

        // Only one part supplied — match on whichever is present.
        if (hasPosition)
            return books.FirstOrDefault(PositionMatches);
        if (hasTitle)
            return books.FirstOrDefault(TitleMatches);

        return null;
    }

    public async Task<Series> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions)
    {
        var (row, _) = await UpsertByNameAsync(
            seriesName,
            row => row.IncludeOmnibusEditions = includeOmnibusEditions,
            () => new Series { Name = seriesName, IncludeOmnibusEditions = includeOmnibusEditions });
        return row;
    }

    public async Task<(Series Series, bool Created)> GetOrCreateByNameAsync(string name)
    {
        var (row, created) = await UpsertByNameAsync(name, _ => { }, () => new Series { Name = name });
        return (row, created);
    }

    /// <summary>
    /// Deletes one catalog row, but only when it is still the untouched shell a create path just
    /// inserted: unmatched, no omnibus flag, no mapping patterns and no roster. The emptiness is
    /// checked in the same statement as the delete, so a concurrent write - a mapping another
    /// request inserted onto the row, a match, a roster replace, an omnibus toggle - makes the
    /// condition fail at delete time and the row survives, instead of being cascaded away with the
    /// other request's data. Only the series-mapping create path's rollback uses it, and only for
    /// a row <c>SeriesService.CreateSeriesMappingAsync</c> itself just created; this conditional
    /// re-check is what keeps that rollback safe against a concurrent request that adopted the row.
    /// Returns whether the row was actually deleted.
    /// </summary>
    public async Task<bool> DeleteIfEmptyAsync(long id)
    {
        var deletedRows = await _db.Series
            .Where(s => s.Id == id
                && s.MatchedSourceName == null
                && s.MatchedSourceId == null
                && !s.IncludeOmnibusEditions
                && !s.Mappings.Any()
                && !s.ExpectedBooks.Any())
            .ExecuteDeleteAsync();

        if (deletedRows > 0)
        {
            // ExecuteDeleteAsync bypasses the change tracker: the row this call inserted is still
            // tracked here, and a deleted rowid SQLite may hand to a later insert must never
            // resolve back to it inside this request-scoped context.
            foreach (var entry in _db.ChangeTracker.Entries<Series>().Where(e => e.Entity.Id == id).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        return deletedRows > 0;
    }

    /// <summary>
    /// Re-keys a catalog row from <paramref name="oldName"/> to <paramref name="newName"/>.
    /// Only <see cref="Series.Name"/> is rewritten - the row's id and its expected-book
    /// children (roster, ignore flags, source urls, years) are untouched, so no child rows
    /// need re-pointing and nothing else in the schema references the row by name.
    /// </summary>
    public async Task<Series> RenameAsync(string oldName, string newName)
    {
        var existing = await _db.Series.FirstOrDefaultAsync(s => s.Name == oldName)
            ?? throw new KeyNotFoundException($"Series '{oldName}' not found");

        existing.Name = newName;
        try
        {
            await _db.SaveChangesAsync();
            return existing;
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // series.name is unique and a row already owns the destination. A rename here would
            // silently merge two rosters (or clobber one); detach so the half-mutated loser never
            // stays in the identity map for the caller, then report it as an adoption failure the
            // batch can count rather than a raw constraint 500.
            _db.Entry(existing).State = EntityState.Detached;
            throw new InvalidOperationException($"A series named '{newName}' already exists.");
        }
    }

    /// <summary>
    /// Deletes the catalog row for a series, along with the expected books that belonged to it
    /// alone. <c>expected_books.series_id</c> is SET NULL rather than cascading, so the delete
    /// first unlinks every expected book of this series (<see cref="ExpectedBook.SeriesId"/> to
    /// null), removes the row, then deletes the orphans the unlink produced - books with no
    /// series and no author links. A book the author rosters also report survives as an
    /// author-linked row; a book another series reports keeps its series link. Mappings cascade
    /// via their FK as before. Set-based deletes bypass the change tracker, so tracked copies of
    /// the deleted/unlinked rows are detached (see <see cref="DeleteIfEmptyAsync"/>).
    /// </summary>
    public async Task<bool> DeleteSeriesAsync(string name)
    {
        var row = await _db.Series.FirstOrDefaultAsync(s => s.Name == name);
        if (row is null)
        {
            return false;
        }

        // Deliberately the repository-level cleanup, not the FK's SET NULL alone: the FK would
        // unlink the rows but leave series-less bookless rows behind, while this deletes exactly
        // the orphans (and keeps the author-linked ones).
        await _expectedBookRepository.UnlinkSeriesBooksAsync(row.Id, new List<long>());

        var deletedRows = await _db.Series
            .Where(s => s.Id == row.Id)
            .ExecuteDeleteAsync();

        await _expectedBookRepository.DeleteOrphanExpectedBooksAsync();

        if (deletedRows > 0)
        {
            foreach (var entry in _db.ChangeTracker.Entries<Series>().Where(e => e.Entity.Id == row.Id).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        return deletedRows > 0;
    }
}
