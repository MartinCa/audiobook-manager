using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class HardcoverQuotaRepository : IHardcoverQuotaRepository
{
    private readonly DatabaseContext _db;

    public HardcoverQuotaRepository(DatabaseContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Atomically consumes one unit of the day's budget, returning false when it is spent.
    ///
    /// The compare-and-increment has to happen inside a single SQL statement. Hardcover requests
    /// are issued concurrently (ScrapingService.SearchMultiple fans out across sources, and
    /// HardcoverRetryHandler re-enters on a retry), so a read-modify-write in C# lets two callers
    /// both read the same count and write count+1 - a lost update that quietly overruns the
    /// daily limit. The insert path races the same way and is resolved by letting the unique
    /// index on utc_date reject the loser, which then retries against the winner's row.
    /// </summary>
    public async Task<bool> TryConsumeAsync(DateOnly utcDate, int dailyLimit)
    {
        if (dailyLimit < 1)
        {
            return false;
        }

        var updated = await _db.HardcoverRequestQuotas
            .Where(q => q.UtcDate == utcDate && q.RequestCount < dailyLimit)
            .ExecuteUpdateAsync(setters => setters.SetProperty(q => q.RequestCount, q => q.RequestCount + 1));

        if (updated > 0)
        {
            return true;
        }

        // No row was updated: either today's row does not exist yet, or the budget is spent.
        if (await _db.HardcoverRequestQuotas.AnyAsync(q => q.UtcDate == utcDate))
        {
            return false;
        }

        try
        {
            var inserted = new HardcoverRequestQuota { UtcDate = utcDate, RequestCount = 1 };
            _db.HardcoverRequestQuotas.Add(inserted);
            await _db.SaveChangesAsync();

            // ExecuteUpdateAsync writes straight to the database without going through the
            // change tracker, so a row left tracked here would keep reporting the count it had
            // at insert time for the rest of the scope. Detach it so every later read is a
            // fresh database read.
            _db.Entry(inserted).State = EntityState.Detached;
            return true;
        }
        catch (DbUpdateException)
        {
            // Another request inserted today's row first; retry against it.
            _db.ChangeTracker.Clear();

            var retried = await _db.HardcoverRequestQuotas
                .Where(q => q.UtcDate == utcDate && q.RequestCount < dailyLimit)
                .ExecuteUpdateAsync(setters => setters.SetProperty(q => q.RequestCount, q => q.RequestCount + 1));

            return retried > 0;
        }
    }

    public async Task<int> GetCountAsync(DateOnly utcDate)
    {
        // AsNoTracking for the same reason: the counter is only ever moved by ExecuteUpdateAsync,
        // so a tracked instance would shadow the real value.
        var row = await _db.HardcoverRequestQuotas
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.UtcDate == utcDate);
        return row?.RequestCount ?? 0;
    }
}
