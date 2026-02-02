using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class LibraryScanService : ILibraryScanService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IDiscoveredAudiobookRepository _discoveredAudiobookRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly ILogger<LibraryScanService> _logger;

    public LibraryScanService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IDiscoveredAudiobookRepository discoveredAudiobookRepository,
        IAudiobookTagHandler tagHandler,
        ILogger<LibraryScanService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _discoveredAudiobookRepository = discoveredAudiobookRepository;
        _tagHandler = tagHandler;
        _logger = logger;
    }

    public async Task<(int TotalFiles, int NewFiles, int TrackedFiles)> ScanLibrary(Func<string, int, int, Task> progressAction)
    {
        _logger.LogInformation("Starting library scan of {LibraryPath}", _settings.AudiobookLibraryPath);

        await _discoveredAudiobookRepository.ClearAllAsync();

        var files = FileScanner.ScanDirectoryForFiles(
            _settings.AudiobookLibraryPath,
            AudiobookTagHandler.IsSupported);

        var knownPaths = await _audiobookRepository.GetAllFilePathsAsync();

        var totalFiles = files.Count;
        var filesScanned = 0;
        var newFilesDiscovered = 0;

        foreach (var file in files)
        {
            filesScanned++;

            if (knownPaths.Contains(file.FullPath))
            {
                await progressAction($"Already tracked: {file.FileName}", filesScanned, totalFiles);
                continue;
            }

            try
            {
                var fileInfo = new FileInfo(file.FullPath);
                var parsed = _tagHandler.ParseAudiobook(fileInfo);

                var discovered = new DiscoveredAudiobook(
                    parsed.BookName ?? file.FileName,
                    file.FullPath,
                    file.FileName,
                    file.SizeInBytes,
                    DateTime.UtcNow)
                {
                    Subtitle = parsed.Subtitle,
                    Series = parsed.Series,
                    SeriesPart = parsed.SeriesPart,
                    Year = parsed.Year,
                    Authors = parsed.Authors.Count > 0 ? string.Join(", ", parsed.Authors.Select(a => a.Name)) : null,
                    Narrators = parsed.Narrators.Count > 0 ? string.Join(", ", parsed.Narrators.Select(n => n.Name)) : null,
                    Genres = parsed.Genres.Count > 0 ? string.Join(", ", parsed.Genres) : null,
                    Description = parsed.Description,
                    Copyright = parsed.Copyright,
                    Publisher = parsed.Publisher,
                    Rating = parsed.Rating,
                    Asin = parsed.Asin,
                    Www = parsed.Www,
                    DurationInSeconds = parsed.DurationInSeconds
                };

                await _discoveredAudiobookRepository.InsertAsync(discovered);
                newFilesDiscovered++;

                await progressAction($"Discovered: {file.FileName}", filesScanned, totalFiles);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse audiobook at {FilePath}", file.FullPath);
                await progressAction($"Error parsing: {file.FileName}", filesScanned, totalFiles);
            }
        }

        _logger.LogInformation("Library scan complete. Total: {Total}, New: {New}, Tracked: {Tracked}",
            totalFiles, newFilesDiscovered, totalFiles - newFilesDiscovered);

        return (totalFiles, newFilesDiscovered, totalFiles - newFilesDiscovered);
    }
}
