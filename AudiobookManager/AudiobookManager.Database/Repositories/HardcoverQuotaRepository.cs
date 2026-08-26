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

    public async Task<bool> TryConsumeAsync(DateOnly utcDate, int dailyLimit)
    {
        var row = await _db.HardcoverRequestQuotas.FirstOrDefaultAsync(q => q.UtcDate == utcDate);

        if (row is null)
        {
            if (dailyLimit < 1)
            {
                return false;
            }

            _db.HardcoverRequestQuotas.Add(new HardcoverRequestQuota { UtcDate = utcDate, RequestCount = 1 });
            await _db.SaveChangesAsync();
            return true;
        }

        if (row.RequestCount >= dailyLimit)
        {
            return false;
        }

        row.RequestCount++;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetCountAsync(DateOnly utcDate)
    {
        var row = await _db.HardcoverRequestQuotas.FirstOrDefaultAsync(q => q.UtcDate == utcDate);
        return row?.RequestCount ?? 0;
    }
}
