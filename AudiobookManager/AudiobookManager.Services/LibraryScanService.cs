using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainAudiobookFileInfo = AudiobookManager.Domain.AudiobookFileInfo;

namespace AudiobookManager.Services;

public class LibraryScanService : ILibraryScanService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IDiscoveredAudiobookRepository _discoveredAudiobookRepository;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookService _audiobookService;
    private readonly ILogger<LibraryScanService> _logger;

    public LibraryScanService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IDiscoveredAudiobookRepository discoveredAudiobookRepository,
        IAudiobookTagHandler tagHandler,
        IAudiobookService audiobookService,
        ILogger<LibraryScanService> logger)
    {
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _discoveredAudiobookRepository = discoveredAudiobookRepository;
        _tagHandler = tagHandler;
        _audiobookService = audiobookService;
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

    public async Task<(int Processed, int Succeeded, int Failed)> BulkImportAsync(
        List<string> filePaths,
        Func<int, int, int, int, Task> progressAction,
        Func<string, string, Task>? onItemFailed = null)
    {
        var discovered = await _discoveredAudiobookRepository.GetByPathsAsync(filePaths);
        var byPath = discovered.ToDictionary(d => d.FileInfoFullPath);

        return await BulkOperationRunner.RunAsync(
            filePaths,
            async path =>
            {
                try
                {
                    if (!byPath.TryGetValue(path, out var entry))
                    {
                        throw new InvalidOperationException($"Discovered audiobook not found for path {path}");
                    }

                    var domain = ToDomainAudiobook(entry);
                    await _audiobookService.OrganizeAudiobook(domain, (_, __) => Task.CompletedTask);
                    await _discoveredAudiobookRepository.DeleteAsync(entry.Id);
                }
                catch (Exception ex) when (onItemFailed is not null)
                {
                    await onItemFailed(path, ex.Message);
                    throw;
                }
            },
            _logger,
            path => $"Failed to bulk import discovered audiobook at {path}",
            progressAction);
    }

    public async Task<bool> IsDuplicateTargetAsync(DiscoveredAudiobook entry)
    {
        try
        {
            var domain = ToDomainAudiobook(entry);
            var targetPath = _audiobookService.GenerateLibraryPath(domain);
            if (!File.Exists(targetPath))
            {
                return false;
            }

            // The discovered file may already sit at its own computed target path (e.g. it was
            // scanned in place inside the library). That's not a collision with a different
            // file - it's the same file - so it must not be flagged as a duplicate. File systems
            // are case-insensitive on Windows/macOS but case-sensitive on Linux.
            var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return !string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(entry.FileInfoFullPath), pathComparison);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Builds the domain object from the tag snapshot captured at scan time, rather than
    // re-parsing the file, so the imported book matches exactly what the user reviewed and
    // accepted in the discovered-books list.
    private static DomainAudiobook ToDomainAudiobook(DiscoveredAudiobook entry)
    {
        var authors = AudiobookTagHandler.ParsePersonsFromString(entry.Authors ?? string.Empty);
        if (authors.Count == 0 || string.IsNullOrWhiteSpace(entry.BookName) || !entry.Year.HasValue)
        {
            throw new InvalidOperationException("Missing required tags: author, book name, and year are all required");
        }

        var genres = string.IsNullOrWhiteSpace(entry.Genres)
            ? new List<string>()
            : entry.Genres.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return new DomainAudiobook(
            authors,
            entry.BookName,
            entry.Year,
            new DomainAudiobookFileInfo(entry.FileInfoFullPath, entry.FileInfoFileName, entry.FileInfoSizeInBytes))
        {
            Narrators = AudiobookTagHandler.ParsePersonsFromString(entry.Narrators ?? string.Empty),
            Subtitle = entry.Subtitle,
            Series = entry.Series,
            SeriesPart = entry.SeriesPart,
            Genres = genres,
            Description = entry.Description,
            Copyright = entry.Copyright,
            Publisher = entry.Publisher,
            Rating = entry.Rating,
            Asin = entry.Asin,
            Www = entry.Www,
            DurationInSeconds = entry.DurationInSeconds
        };
    }
}
