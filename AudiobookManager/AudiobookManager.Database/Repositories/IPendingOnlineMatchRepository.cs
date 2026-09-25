using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IPendingOnlineMatchRepository
{
    Task<PendingOnlineMatch> UpsertAsync(PendingOnlineMatch match);

    Task<PendingOnlineMatch?> GetByAudiobookIdAsync(long audiobookId);

    Task<bool> DeleteByAudiobookIdAsync(long audiobookId);

    Task<bool> SetStatusAsync(long audiobookId, PendingOnlineMatchStatus status);

    Task<(List<PendingOnlineMatch> Items, int TotalCount)> GetPageWithAudiobookAsync(
        PendingOnlineMatchStatus status, int skip, int take);
}
