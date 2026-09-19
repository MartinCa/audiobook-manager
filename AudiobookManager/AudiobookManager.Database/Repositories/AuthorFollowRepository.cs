using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;

public class AuthorFollowRepository : IAuthorFollowRepository
{
    private readonly DatabaseContext _db;

    public AuthorFollowRepository(DatabaseContext db)
    {
        _db = db;
    }

    public Task<bool> IsFollowedAsync(long personId) =>
        _db.AuthorFollows.AsNoTracking().AnyAsync(f => f.PersonId == personId);

    public async Task FollowAsync(long personId)
    {
        var exists = await _db.AuthorFollows.AnyAsync(f => f.PersonId == personId);
        if (exists)
        {
            return;
        }

        _db.AuthorFollows.Add(new AuthorFollow { PersonId = personId, CreatedAt = DateTime.UtcNow });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
        {
            // Lost a race against a concurrent follow of the same author - the row is already
            // there, which is exactly what this call asked for.
        }
    }

    public async Task UnfollowAsync(long personId)
    {
        await _db.AuthorFollows
            .Where(f => f.PersonId == personId)
            .ExecuteDeleteAsync();
    }

    public async Task<List<Person>> GetFollowedMatchedAuthorsAsync()
    {
        return await _db.AuthorFollows
            .AsNoTracking()
            .Where(f => f.Person.HardcoverAuthorId != null && f.Person.HardcoverAuthorId != "")
            .Select(f => f.Person)
            .ToListAsync();
    }
}
