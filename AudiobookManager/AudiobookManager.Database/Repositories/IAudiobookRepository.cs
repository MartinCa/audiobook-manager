using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IAudiobookRepository
{
    Task<Audiobook> InsertAudiobook(Audiobook audiobook);
    Task<HashSet<string>> GetAllFilePathsAsync();
    Task<(List<Audiobook> Items, int Total)> GetAllAsync(int limit, int offset);
    Task<(List<Audiobook> Items, int Total)> SearchAsync(string query, int limit, int offset);
    Task<List<Audiobook>> GetBooksBySeriesAsync(string seriesName, long? authorId);
    Task<Audiobook?> GetByIdWithIncludesAsync(long id);
    Task<List<Audiobook>> GetAllWithIncludesAsync();
    Task UpdateFilePathAsync(long id, string newFullPath, string newFileName);
    Task UpdateCoverFilePathAsync(long id, string? coverFilePath);
    Task DeleteAudiobookAsync(long id);
}
