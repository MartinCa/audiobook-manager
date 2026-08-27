using System.Diagnostics;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AudiobookDb = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;
public class AudiobookService : IAudiobookService
{
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<AudiobookService> _logger;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPersonRepository _personRepository;
    private readonly IGenreRepository _genreRepository;

    public AudiobookService(IAudiobookTagHandler tagHandler, IOptions<AudiobookManagerSettings> settings, IAudiobookRepository audiobookRepository, IPersonRepository personRepository, IGenreRepository genreRepository, ILogger<AudiobookService> logger)
    {
        _tagHandler = tagHandler;
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _personRepository = personRepository;
        _genreRepository = genreRepository;
        _logger = logger;
    }

    public Audiobook ParseAudiobook(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        return _tagHandler.ParseAudiobook(fileInfo);
    }

    public string GenerateLibraryPath(Audiobook audiobook)
    {
        var newRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);
        return AudiobookFileHandler.JoinPaths(_settings.AudiobookLibraryPath, newRelativePath);
    }

    public async Task<Audiobook> OrganizeAudiobook(Audiobook audiobook, Func<string, int, Task> progressAction)
    {
        var oldDirectory = Path.GetDirectoryName(audiobook.FileInfo.FullPath);

        var sw = new Stopwatch();
        sw.Start();

        await progressAction("Started", 0);
        const int afterTagsProgress = 70;
        int lastProgressNotified = 0;

        Action<float> saveTagsProgressAction = (float progress) =>
        {
            var modifiedProgress = (int)(afterTagsProgress * progress);
            if (modifiedProgress - lastProgressNotified >= 10)
            {
                lastProgressNotified = modifiedProgress;
                _logger.LogInformation("({audiobookFile}) saving tags progress {progress}, full progress {modifiedProgress}", audiobook.FileInfo.FullPath, progress, modifiedProgress);
                progressAction("Saving tags", modifiedProgress);
            }
        };

        _tagHandler.SaveAudiobookTagsToFile(audiobook, saveTagsProgressAction);

        _logger.LogInformation("({audiobookFile}) Saving tags to file took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, sw.ElapsedMilliseconds);

        await progressAction("Saved tags", afterTagsProgress);

        var newFullPath = GenerateLibraryPath(audiobook);

        newFullPath = await RelocateIfPathChangedAsync(audiobook, newFullPath, oldDirectory, sw, progressAction, relocatingProgress: 75, relocatedProgress: 80);

        var newParsed = ParseAudiobook(newFullPath);

        await progressAction("Reparsed", 85);

        AudiobookFileHandler.WriteMetadata(newParsed);

        await progressAction("Written metadata files", 90);

        newParsed.CoverFilePath = AudiobookFileHandler.WriteCover(newParsed);

        await progressAction("Written cover", 95);

        _logger.LogInformation("({audiobookFile}) Writing metadata files took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, sw.ElapsedMilliseconds);

        await InsertAudiobook(newParsed);

        await progressAction("Done", 100);

        return newParsed;
    }

    /// <summary>
    /// Moves the audiobook's file to <paramref name="newFullPath"/> if it differs from its current path,
    /// throwing if a file already occupies the destination, and cleans up sidecar files left behind in
    /// <paramref name="oldDirectory"/> when it moved to a different directory. No-ops (no exists-check,
    /// no move, no logging/progress) when the path is unchanged. Returns the resulting path.
    /// </summary>
    private async Task<string> RelocateIfPathChangedAsync(
        Audiobook audiobook,
        string newFullPath,
        string oldDirectory,
        Stopwatch? sw = null,
        Func<string, int, Task>? progressAction = null,
        int relocatingProgress = 0,
        int relocatedProgress = 0)
    {
        if (newFullPath == audiobook.FileInfo.FullPath)
        {
            return newFullPath;
        }

        if (File.Exists(newFullPath))
        {
            throw new Exception($"'{newFullPath}' already exists");
        }

        if (progressAction is not null)
        {
            await progressAction("Generated new path, relocating", relocatingProgress);
        }

        sw?.Restart();

        AudiobookFileHandler.RelocateAudiobook(audiobook, newFullPath);

        if (sw is not null)
        {
            _logger.LogInformation("({audiobookFile}) Relocating to {newFullPath} took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, newFullPath, sw.ElapsedMilliseconds);
            sw.Restart();
        }

        if (progressAction is not null)
        {
            await progressAction("Relocated", relocatedProgress);
        }

        if (oldDirectory != Path.GetDirectoryName(newFullPath))
        {
            AudiobookFileHandler.RemoveSidecarFiles(oldDirectory);
            AudiobookFileHandler.RemoveDirIfEmpty(oldDirectory);
        }

        return newFullPath;
    }

    private async Task<(List<Database.Models.Person> Authors, List<Database.Models.Person> Narrators, List<Database.Models.Genre> Genres)> GetOrCreateAuthorsNarratorsGenres(Audiobook audiobook)
    {
        var authors = new List<Database.Models.Person>();
        foreach (var author in audiobook.Authors)
            authors.Add(await _personRepository.GetOrCreatePerson(author.Name));

        var narrators = new List<Database.Models.Person>();
        foreach (var narrator in audiobook.Narrators)
            narrators.Add(await _personRepository.GetOrCreatePerson(narrator.Name));

        var genres = new List<Database.Models.Genre>();
        foreach (var genre in audiobook.Genres)
            genres.Add(await _genreRepository.GetOrCreateGenre(genre));

        return (authors, narrators, genres);
    }

    public async Task<Audiobook> InsertAudiobook(Audiobook audiobook)
    {
        var (authors, narrators, genres) = await GetOrCreateAuthorsNarratorsGenres(audiobook);

        AudiobookDb dbAudiobook = new AudiobookDb(
            audiobook.Id ?? default,
            audiobook.BookName,
            audiobook.Subtitle,
            audiobook.Series,
            audiobook.SeriesPart,
            audiobook.Year ?? 0,
            audiobook.Description,
            audiobook.Copyright,
            audiobook.Publisher,
            audiobook.Rating,
            audiobook.Asin,
            audiobook.Www,
            audiobook.CoverFilePath,
            audiobook.DurationInSeconds,
            audiobook.FileInfo.FullPath,
            audiobook.FileInfo.FileName,
            audiobook.FileInfo.SizeInBytes
            )
        {
            Authors = authors,
            Narrators = narrators,
            Genres = genres
        };

        var result = await _audiobookRepository.InsertAudiobook(dbAudiobook);
        return FromDb(result);
    }

    public async Task<Audiobook?> GetAudiobookById(long id)
    {
        var dbAudiobook = await _audiobookRepository.GetByIdWithIncludesAsync(id);
        if (dbAudiobook == null) return null;

        var domain = FromDb(dbAudiobook);
        domain.Id = dbAudiobook.Id;
        return domain;
    }

    public async Task<Audiobook> UpdateAudiobook(long id, Audiobook audiobook)
    {
        var existing = await _audiobookRepository.GetByIdWithIncludesAsync(id);
        if (existing == null)
            throw new Exception($"Audiobook with id {id} not found");

        var oldFilePath = existing.FileInfoFullPath;
        var oldDirectory = Path.GetDirectoryName(oldFilePath);

        // Save tags to the m4b file
        audiobook.FileInfo = new AudiobookFileInfo(existing.FileInfoFullPath, existing.FileInfoFileName, existing.FileInfoSizeInBytes);
        _tagHandler.SaveAudiobookTagsToFile(audiobook, _ => { });

        // Check if the file needs to be relocated
        var newFullPath = GenerateLibraryPath(audiobook);
        newFullPath = await RelocateIfPathChangedAsync(audiobook, newFullPath, oldDirectory);
        audiobook.FileInfo = new AudiobookFileInfo(newFullPath, Path.GetFileName(newFullPath), audiobook.FileInfo.SizeInBytes);

        // Re-parse from current location to get updated metadata
        var currentPath = audiobook.FileInfo.FullPath;
        var newParsed = ParseAudiobook(currentPath);

        // Write sidecar files
        AudiobookFileHandler.WriteMetadata(newParsed);
        newParsed.CoverFilePath = AudiobookFileHandler.WriteCover(newParsed);

        // Update DB record
        var (authors, narrators, genres) = await GetOrCreateAuthorsNarratorsGenres(audiobook);

        existing.BookName = audiobook.BookName;
        existing.Subtitle = audiobook.Subtitle;
        existing.Series = audiobook.Series;
        existing.SeriesPart = audiobook.SeriesPart;
        existing.Year = audiobook.Year ?? existing.Year;
        existing.Description = audiobook.Description;
        existing.Copyright = audiobook.Copyright;
        existing.Publisher = audiobook.Publisher;
        existing.Rating = audiobook.Rating;
        existing.Asin = audiobook.Asin;
        existing.Www = audiobook.Www;
        existing.CoverFilePath = newParsed.CoverFilePath;
        existing.DurationInSeconds = newParsed.DurationInSeconds;
        existing.FileInfoFullPath = audiobook.FileInfo.FullPath;
        existing.FileInfoFileName = audiobook.FileInfo.FileName;
        existing.FileInfoSizeInBytes = audiobook.FileInfo.SizeInBytes;
        existing.Authors = authors;
        existing.Narrators = narrators;
        existing.Genres = genres;

        await _audiobookRepository.UpdateAudiobookAsync(existing);

        return FromDb(existing);
    }

    public static Audiobook FromDb(AudiobookDb audiobookDb)
    {
        return new Audiobook(
            audiobookDb.Authors.Select(FromDbPerson).ToList(),
            audiobookDb.BookName,
            audiobookDb.Year,
            new AudiobookFileInfo(audiobookDb.FileInfoFullPath, audiobookDb.FileInfoFileName, audiobookDb.FileInfoSizeInBytes))
        {
            Narrators = audiobookDb.Narrators.Select(FromDbPerson).ToList(),
            BookName = audiobookDb.BookName,
            Subtitle = audiobookDb.Subtitle,
            Series = audiobookDb.Series,
            SeriesPart = audiobookDb.SeriesPart,
            Genres = audiobookDb.Genres.Select(x => x.Name).ToList(),
            Description = audiobookDb.Description,
            Copyright = audiobookDb.Copyright,
            Publisher = audiobookDb.Publisher,
            Rating = audiobookDb.Rating,
            Asin = audiobookDb.Asin,
            Www = audiobookDb.Www,
            CoverFilePath = audiobookDb.CoverFilePath,
            DurationInSeconds = audiobookDb.DurationInSeconds
        };
    }

    public static Person FromDbPerson(Database.Models.Person personDb)
    {
        return new Person(personDb.Name) { Id = personDb.Id };
    }
}
