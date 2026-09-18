using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IUpcomingReleaseRepository
{
    /// <summary>
    /// Inserts a newly discovered release, or - when a row for the same
    /// (<see cref="UpcomingRelease.SourceName"/>, <see cref="UpcomingRelease.SourceBookId"/>)
    /// already exists - fills in whichever of <see cref="UpcomingRelease.PersonId"/> /
    /// <see cref="UpcomingRelease.SeriesId"/> the existing row is still missing, without
    /// clobbering a link a different poll already set. Never updates the title/date/links of an
    /// already-known release: the row is the user-visible record from first discovery, and a
    /// source correcting itself later is not this feature's concern.
    /// </summary>
    Task UpsertAsync(UpcomingRelease release);

    Task<(List<UpcomingRelease> Items, int Total)> GetPagedAsync(
        long? personId, long? seriesId, int limit, int offset);

    /// <summary>Deletes one release by id. Returns whether a row was actually deleted.</summary>
    Task<bool> DeleteAsync(long id);
}
