using AudiobookManager.Database.Models;

using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class LibrarySettingsRepository : ILibrarySettingsRepository
{
    private readonly DatabaseContext _db;

    public LibrarySettingsRepository(DatabaseContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The settings row, creating it on first access so callers never handle a null. The row's
    /// id is the fixed SingletonId, so the primary key is the guard: two scopes racing the
    /// bootstrap both insert id 1 and only one commit wins; the loser re-reads the winner's row
    /// (the same adopt-the-winner pattern PersonRepository.GetOrCreatePersons uses against its
    /// unique index).
    /// </summary>
    public async Task<LibrarySettings> GetOrCreateAsync()
    {
        var settings = await _db.LibrarySettings.AsNoTracking().SingleOrDefaultAsync();
        if (settings != null)
        {
            return settings;
        }

        var created = new LibrarySettings(LibrarySettings.SingletonId, InitialsSpacing.Unspaced);
        _db.LibrarySettings.Add(created);

        try
        {
            await _db.SaveChangesAsync();
            return created;
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            _db.Entry(created).State = EntityState.Detached;
            return await _db.LibrarySettings.AsNoTracking().SingleAsync();
        }
    }

    public async Task<LibrarySettings> UpdateAsync(InitialsSpacing initialsSpacing)
    {
        var settings = await _db.LibrarySettings.SingleOrDefaultAsync();
        if (settings == null)
        {
            settings = new LibrarySettings(LibrarySettings.SingletonId, initialsSpacing);
            _db.LibrarySettings.Add(settings);
        }
        else
        {
            settings.InitialsSpacing = initialsSpacing;
        }

        await _db.SaveChangesAsync();
        return settings;
    }
}
