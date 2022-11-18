using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Options;
using AudiobookDb = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;
public class AudiobookService : IAudiobookService
{
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly AudiobookManagerSettings _settings;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPersonRepository _personRepository;
    private readonly IGenreRepository _genreRepository;

    public AudiobookService(IAudiobookTagHandler tagHandler, IOptions<AudiobookManagerSettings> settings, IAudiobookRepository audiobookRepository, IPersonRepository personRepository, IGenreRepository genreRepository)
    {
        _tagHandler = tagHandler;
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _personRepository = personRepository;
        _genreRepository = genreRepository;
    }

    public Audiobook ParseAudiobook(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        return _tagHandler.ParseAudiobook(fileInfo);
    }

    public Audiobook OrganizeAudiobook(Audiobook audiobook)
    {
        if (string.IsNullOrEmpty(audiobook.FileInfo?.FullPath))
        {
            throw new Exception("No full path for audiobook");
        }

        var oldDirectory = Path.GetDirectoryName(audiobook.FileInfo.FullPath);

        _tagHandler.SaveAudiobookTagsToFile(audiobook);

        var newRelativePath = _tagHandler.GenerateRelativeAudiobookPath(audiobook);
        var newFullPath = Path.Join(_settings.AudiobookLibraryPath, newRelativePath);

        if (File.Exists(newFullPath))
        {
            throw new Exception($"File '{newFullPath}' already exists");
        }

        AudiobookFileHandler.RelocateAudiobook(audiobook, newFullPath);
        var newParsed = ParseAudiobook(newFullPath);

        AudiobookFileHandler.WriteMetadata(newParsed);

        newParsed.CoverFilePath = AudiobookFileHandler.WriteCover(newParsed);

        AudiobookFileHandler.RemoveDirIfEmpty(oldDirectory);

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
            audiobookDb.Year)
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
            DurationInSeconds = audiobookDb.DurationInSeconds,
            FileInfo = new AudiobookFileInfo(audiobookDb.FileInfoFullPath, audiobookDb.FileInfoFileName, audiobookDb.FileInfoSizeInBytes)
        };
    }

    public static Person FromDbPerson(Database.Models.Person personDb)
    {
        return new Person(personDb.Name) { Id = personDb.Id };
    }
}
