using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DbPerson = AudiobookManager.Database.Models.Person;
using DbGenre = AudiobookManager.Database.Models.Genre;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class AudiobookServiceTests
{
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IPersonRepository> _personRepository = null!;
    private Mock<IGenreRepository> _genreRepository = null!;
    private Mock<ILogger<AudiobookService>> _logger = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private AudiobookService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _personRepository = new Mock<IPersonRepository>();
        _genreRepository = new Mock<IGenreRepository>();
        _logger = new Mock<ILogger<AudiobookService>>();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = "/library"
        });

        _service = new AudiobookService(
            _tagHandler.Object,
            _settings,
            _audiobookRepository.Object,
            _personRepository.Object,
            _genreRepository.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task InsertAudiobook_CreatesPersonsAndGenres()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author1"), new Person("Author2") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/path/test.m4b", "test.m4b", 1000))
        {
            Narrators = new List<Person> { new Person("Narrator1") },
            Genres = new List<string> { "Fiction", "Sci-Fi" }
        };

        _personRepository.Setup(r => r.GetOrCreatePerson("Author1"))
            .ReturnsAsync(new DbPerson(1, "Author1"));
        _personRepository.Setup(r => r.GetOrCreatePerson("Author2"))
            .ReturnsAsync(new DbPerson(2, "Author2"));
        _personRepository.Setup(r => r.GetOrCreatePerson("Narrator1"))
            .ReturnsAsync(new DbPerson(3, "Narrator1"));

        _genreRepository.Setup(r => r.GetOrCreateGenre("Fiction"))
            .ReturnsAsync(new DbGenre(1, "Fiction"));
        _genreRepository.Setup(r => r.GetOrCreateGenre("Sci-Fi"))
            .ReturnsAsync(new DbGenre(2, "Sci-Fi"));

        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 1;
                return db;
            });

        var result = await _service.InsertAudiobook(audiobook);

        _personRepository.Verify(r => r.GetOrCreatePerson("Author1"), Times.Once);
        _personRepository.Verify(r => r.GetOrCreatePerson("Author2"), Times.Once);
        _personRepository.Verify(r => r.GetOrCreatePerson("Narrator1"), Times.Once);
        _genreRepository.Verify(r => r.GetOrCreateGenre("Fiction"), Times.Once);
        _genreRepository.Verify(r => r.GetOrCreateGenre("Sci-Fi"), Times.Once);
        _audiobookRepository.Verify(r => r.InsertAudiobook(It.Is<DbAudiobook>(db =>
            db.BookName == "Test Book" &&
            db.Authors.Count == 2 &&
            db.Narrators.Count == 1 &&
            db.Genres.Count == 2
        )), Times.Once);

        Assert.AreEqual("Test Book", result.BookName);
    }

    [TestMethod]
    public async Task InsertAudiobook_EmptyAuthorsAndNarrators_Succeeds()
    {
        var audiobook = new Audiobook(
            new List<Person>(),
            "Solo Book",
            2024,
            new AudiobookFileInfo("/path/solo.m4b", "solo.m4b", 500));

        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 1;
                return db;
            });

        var result = await _service.InsertAudiobook(audiobook);

        _personRepository.Verify(r => r.GetOrCreatePerson(It.IsAny<string>()), Times.Never);
        _genreRepository.Verify(r => r.GetOrCreateGenre(It.IsAny<string>()), Times.Never);
        _audiobookRepository.Verify(r => r.InsertAudiobook(It.Is<DbAudiobook>(db =>
            db.Authors.Count == 0 &&
            db.Narrators.Count == 0 &&
            db.Genres.Count == 0
        )), Times.Once);

        Assert.AreEqual("Solo Book", result.BookName);
    }

    [TestMethod]
    public void GenerateLibraryPath_ReturnsExpectedPath()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("John Smith") },
            "My Book",
            2024,
            new AudiobookFileInfo("/import/test.m4b", "test.m4b", 1000));

        var result = _service.GenerateLibraryPath(audiobook);

        Assert.IsTrue(result.StartsWith("/library"));
        Assert.IsTrue(result.Contains("John Smith"));
        Assert.IsTrue(result.Contains("My Book"));
    }

    [TestMethod]
    public void ParseAudiobook_CallsTagHandler()
    {
        var expected = new Audiobook(
            new List<Person> { new Person("Author") },
            "Parsed Book",
            2024,
            new AudiobookFileInfo("/path/book.m4b", "book.m4b", 1000));

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
            .Returns(expected);

        var result = _service.ParseAudiobook("/path/book.m4b");

        Assert.AreEqual("Parsed Book", result.BookName);
        _tagHandler.Verify(t => t.ParseAudiobook(It.Is<FileInfo>(fi => fi.FullName == "/path/book.m4b")), Times.Once);
    }
}
