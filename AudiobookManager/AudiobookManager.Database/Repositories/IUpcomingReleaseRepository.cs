using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IUpcomingReleaseRepository
{
    /// <summary>
    /// Inserts a newly discovered release, or - when a row for the same
    /// (<see cref="UpcomingRelease.SourceName"/>, <see cref="UpcomingRelease.SourceBookId"/>)
    /// already exists - refreshes it from the freshly-polled data. Title, release date, source
    /// URL and image are always overwritten (a source routinely ships an announced book under a
    /// placeholder title or a provisional date and corrects it later, and the stored row must
    /// track that correction). <see cref="UpcomingRelease.PersonId"/>/<see cref="UpcomingRelease.SeriesId"/>/
    /// <see cref="UpcomingRelease.SeriesPosition"/> are the opposite: filled in only when still
    /// null, never clobbering a link a different poll already set. Never touches
    /// <see cref="UpcomingRelease.DiscoveredAt"/> once set.
    /// </summary>
    Task UpsertAsync(UpcomingRelease release);

    Task<(List<UpcomingRelease> Items, int Total)> GetPagedAsync(
        long? personId, long? seriesId, int limit, int offset);

    /// <summary>Deletes one release by id. Returns whether a row was actually deleted.</summary>
    Task<bool> DeleteAsync(long id);
}
