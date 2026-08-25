using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DbPerson = AudiobookManager.Database.Models.Person;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class SimilarValueServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IPersonRepository> _personRepository = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<ILogger<SimilarValueService>> _logger = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private SimilarValueService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _personRepository = new Mock<IPersonRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<SimilarValueService>>();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookImportPath = "/import",
            AudiobookLibraryPath = "/library"
        });

        _service = new SimilarValueService(
            _audiobookRepository.Object,
            _personRepository.Object,
            _audiobookService.Object,
            _settings,
            _logger.Object);
    }

    private static DbAudiobook MakeDbAudiobook(long id, string bookName, string? series = null)
    {
        return new DbAudiobook(id, bookName, null, series, null, 2024, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);
    }

    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_GroupsNearDuplicateAuthorNames()
    {
        var book1 = MakeDbAudiobook(1, "Book One");
        var book2 = MakeDbAudiobook(2, "Book Two");
        var authors = new List<DbPerson>
        {
            new(1, "J.K. Rowling") { BooksAuthored = new List<DbAudiobook> { book1 } },
            new(2, "JK Rowling") { BooksAuthored = new List<DbAudiobook> { book2 } },
            new(3, "Brandon Sanderson") { BooksAuthored = new List<DbAudiobook>() }
        };
        _personRepository.Setup(r => r.GetAllAuthorsAsync()).ReturnsAsync(authors);

        var groups = await _service.DetectSimilarAuthorsAsync();

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(2, groups[0].Candidates.Count);
        var rowlingCandidate = groups[0].Candidates.First(c => c.Value == "J.K. Rowling");
        CollectionAssert.AreEquivalent(new List<long> { 1 }, rowlingCandidate.AudiobookIds);
    }

    [TestMethod]
    public async Task DetectSimilarSeriesAsync_GroupsNearDuplicateSeriesValues()
    {
        _audiobookRepository.Setup(r => r.GetDistinctSeriesAsync()).ReturnsAsync(
            new Dictionary<string, List<long>>
            {
                ["Fantasy & Adventure"] = new List<long> { 1, 2 },
                ["Fantasy and Adventure"] = new List<long> { 3 },
                ["Mystery"] = new List<long> { 4 }
            });

        var groups = await _service.DetectSimilarSeriesAsync();

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(2, groups[0].Candidates.Count);
    }

    [TestMethod]
    public async Task AlignAuthorsAsync_OneBookFails_OthersStillSucceed()
    {
        var book1 = MakeDbAudiobook(1, "Book One");
        book1.Authors = new List<DbPerson> { new(1, "J.K. Rowling") };
        var book2 = MakeDbAudiobook(2, "Book Two");
        book2.Authors = new List<DbPerson> { new(2, "JK Rowling") };

        _audiobookRepository.Setup(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });

        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()))
            .ThrowsAsync(new Exception("path collision"));
        _audiobookService.Setup(s => s.UpdateAudiobook(2, It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a) => a);

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();

        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling", "JK Rowling" },
            "J.K. Rowling",
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()), Times.Once);
        _audiobookService.Verify(s => s.UpdateAudiobook(2, It.IsAny<Audiobook>()), Times.Once);

        var last = progressCalls.Last();
        Assert.AreEqual(2, last.processed);
        Assert.AreEqual(2, last.total);
        Assert.AreEqual(1, last.succeeded);
        Assert.AreEqual(1, last.failed);
    }

    [TestMethod]
    public async Task AlignAuthorsAsync_DuplicateAuthorAfterAlign_IsDeduplicated()
    {
        var book = MakeDbAudiobook(1, "Book One");
        book.Authors = new List<DbPerson> { new(1, "JK Rowling"), new(2, "J.K. Rowling") };

        _audiobookRepository.Setup(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book });

        Audiobook? capturedAudiobook = null;
        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()))
            .Callback<long, Audiobook>((id, a) => capturedAudiobook = a)
            .ReturnsAsync((long id, Audiobook a) => a);

        await _service.AlignAuthorsAsync(
            new List<string> { "JK Rowling", "J.K. Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        Assert.IsNotNull(capturedAudiobook);
        Assert.AreEqual(1, capturedAudiobook!.Authors.Count);
        Assert.AreEqual("J.K. Rowling", capturedAudiobook.Authors[0].Name);
    }

    [TestMethod]
    public async Task AlignSeriesAsync_UpdatesSeriesForAllAffectedBooks()
    {
        var book1 = MakeDbAudiobook(1, "Book One", "Fantasy & Adventure");
        var book2 = MakeDbAudiobook(2, "Book Two", "Fantasy and Adventure");

        _audiobookRepository.Setup(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });

        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a) => a);

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();

        await _service.AlignSeriesAsync(
            new List<string> { "Fantasy & Adventure", "Fantasy and Adventure" },
            "Fantasy & Adventure",
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.Is<Audiobook>(a => a.Series == "Fantasy & Adventure")), Times.Once);
        _audiobookService.Verify(s => s.UpdateAudiobook(2, It.Is<Audiobook>(a => a.Series == "Fantasy & Adventure")), Times.Once);

        var last = progressCalls.Last();
        Assert.AreEqual(2, last.succeeded);
        Assert.AreEqual(0, last.failed);
    }
}
