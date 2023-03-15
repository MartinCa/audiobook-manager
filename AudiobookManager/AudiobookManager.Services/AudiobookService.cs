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

        _tagHandler.SaveAudiobookTagsToFile(audiobook);

        _logger.LogInformation("({audiobookFile}) Saving tags to file took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, sw.ElapsedMilliseconds);

        await progressAction("Saved tags", 10);

        var newFullPath = GenerateLibraryPath(audiobook);

        if (File.Exists(newFullPath))
        {
            throw new Exception($"'{newFullPath}' already exists");
        }

        await progressAction("Generated new path, relocating", 20);

        sw.Restart();

        AudiobookFileHandler.RelocateAudiobook(audiobook, newFullPath);

        _logger.LogInformation("({audiobookFile}) Relocating to {newFullPath} took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, newFullPath, sw.ElapsedMilliseconds);
        sw.Restart();

        await progressAction("Relocated", 70);

        var newParsed = ParseAudiobook(newFullPath);

        await progressAction("Reparsed", 80);

        AudiobookFileHandler.WriteMetadata(newParsed);

        await progressAction("Written metadata files", 85);

        newParsed.CoverFilePath = AudiobookFileHandler.WriteCover(newParsed);

        await progressAction("Written cover", 90);

        _logger.LogInformation("({audiobookFile}) Writing metadata files took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, sw.ElapsedMilliseconds);

        AudiobookFileHandler.RemoveDirIfEmpty(oldDirectory);

        await InsertAudiobook(newParsed);

        await progressAction("Done", 100);

        return newParsed;
    }

    public async Task<Audiobook> InsertAudiobook(Audiobook audiobook)
    {
        var authorsTasks = audiobook.Authors.Select(x => _personRepository.GetOrCreatePerson(x.Name));
        await Task.WhenAll(authorsTasks);
        var authors = authorsTasks.Select(x => x.Result).ToList();

        var narratorsTasks = audiobook.Narrators.Select(x => _personRepository.GetOrCreatePerson(x.Name));
        await Task.WhenAll(narratorsTasks);
        var narrators = narratorsTasks.Select(x => x.Result).ToList();

        var genresTask = audiobook.Genres.Select(x => _genreRepository.GetOrCreateGenre(x));
        await Task.WhenAll(genresTask);
        var genres = genresTask.Select(x => x.Result).ToList();

        AudiobookDb dbAudiobook = new AudiobookDb(
            audiobook.Id ?? default,
            audiobook.BookName,
            audiobook.Subtitle,
            audiobook.Series,
            audiobook.SeriesPart,
            audiobook.Year.Value,
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
