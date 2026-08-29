using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class SeriesRepository : ISeriesRepository
{
    private readonly DatabaseContext _db;

    public SeriesRepository(DatabaseContext db)
    {
        _db = db;
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
    /// Inserts the series if no row with the same <see cref="Series.Name"/> exists,
    /// otherwise updates the match metadata on the existing row.
    /// </summary>
    public Task<Series> UpsertSeriesAsync(Series series) =>
        UpsertByNameAsync(
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
    /// </summary>
    private async Task<Series> UpsertByNameAsync(string name, Action<Series> applyChanges, Func<Series> createNew)
    {
        var existing = await _db.Series.FirstOrDefaultAsync(s => s.Name == name);
        if (existing is not null)
        {
            applyChanges(existing);
            await _db.SaveChangesAsync();
            return existing;
        }

        var inserted = createNew();
        _db.Series.Add(inserted);

        try
        {
            await _db.SaveChangesAsync();
            return inserted;
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
            return winner;
        }
    }

    public async Task ReplaceExpectedBooksAsync(long seriesId, List<SeriesExpectedBook> expectedBooks)
    {
        // Deliberately the tracked path, not ExecuteDeleteAsync. Series.ExpectedBooks is an
        // inverse navigation, so callers that already loaded the series (MatchSeriesCoreAsync
        // reads the existing roster first) hold a tracked Series whose collection EF keeps
        // fixed up. A set-based delete bypasses the change tracker, leaving the deleted rows
        // both in that collection and in the identity map - and since SQLite reuses deleted
        // rowids, the replacements can be resolved straight back to those ghosts. A roster is
        // tens of rows, so the round trips this costs are not worth that risk.
        var existing = await _db.SeriesExpectedBooks
            .Where(b => b.SeriesId == seriesId)
            .ToListAsync();

        _db.SeriesExpectedBooks.RemoveRange(existing);

        foreach (var book in expectedBooks)
        {
            book.Id = 0;
            book.SeriesId = seriesId;
            _db.SeriesExpectedBooks.Add(book);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<SeriesExpectedBook?> GetExpectedBookAsync(long id)
    {
        return await _db.SeriesExpectedBooks.FindAsync(id);
    }

    /// <summary>
    /// Sets the ignore flag on a roster entry addressed by its natural key. Row ids are not
    /// stable across a re-match or refresh (ReplaceExpectedBooksAsync deletes and re-inserts
    /// the whole roster, and SQLite may hand a deleted rowid to an unrelated new row), so the
    /// entry is located by its series plus position and/or title instead.
    /// </summary>
    public async Task SetExpectedBookIgnoredAsync(string seriesName, string? position, string? title, bool ignored)
    {
        var series = await _db.Series.FirstOrDefaultAsync(s => s.Name == seriesName)
            ?? throw new KeyNotFoundException($"Series '{seriesName}' not found");

        var books = await _db.SeriesExpectedBooks
            .Where(b => b.SeriesId == series.Id)
            .ToListAsync();

        var hasPosition = !string.IsNullOrWhiteSpace(position);
        var hasTitle = !string.IsNullOrWhiteSpace(title);

        bool PositionMatches(SeriesExpectedBook b) =>
            hasPosition && string.Equals(b.Position?.Trim(), position!.Trim(), StringComparison.OrdinalIgnoreCase);

        bool TitleMatches(SeriesExpectedBook b) =>
            hasTitle && string.Equals(b.Title.Trim(), title!.Trim(), StringComparison.OrdinalIgnoreCase);

        // Prefer an entry matching both parts of the key, then fall back to either one alone
        // - a source may report a roster entry without a position at all.
        var book = books.FirstOrDefault(b => PositionMatches(b) && TitleMatches(b))
            ?? books.FirstOrDefault(PositionMatches)
            ?? books.FirstOrDefault(TitleMatches)
            ?? throw new KeyNotFoundException(
                $"Expected book (position '{position}', title '{title}') not found in series '{seriesName}'");

        book.IsIgnored = ignored;
        await _db.SaveChangesAsync();
    }

    public Task<Series> SetIncludeOmnibusEditionsAsync(string seriesName, bool includeOmnibusEditions) =>
        UpsertByNameAsync(
            seriesName,
            row => row.IncludeOmnibusEditions = includeOmnibusEditions,
            () => new Series { Name = seriesName, IncludeOmnibusEditions = includeOmnibusEditions });
}
