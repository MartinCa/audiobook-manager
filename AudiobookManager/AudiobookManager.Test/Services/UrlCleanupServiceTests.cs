using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Moq;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DbPerson = AudiobookManager.Database.Models.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class UrlCleanupServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private AudiobookSaveGate _saveGate = null!;
    private UrlCleanupService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        // A real gate, not a mock: what these tests need to verify is mutual exclusion behavior
        // (a busy book is skipped, not double-processed), which a mocked gate can't exercise.
        _saveGate = new AudiobookSaveGate();
        _service = new UrlCleanupService(
            _audiobookRepository.Object, _audiobookService.Object, _saveGate, Mock.Of<Microsoft.Extensions.Logging.ILogger<UrlCleanupService>>());
    }

    private static DbAudiobook MakeDbAudiobook(long id, string bookName, string? www, List<DbPerson>? authors = null)
    {
        return new DbAudiobook(id, bookName, null, null, null, 2024, null, null, null, null, null, null, www,
            null, null, $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = authors ?? new List<DbPerson>()
        };
    }

    private static DomainAudiobook MakeDomainAudiobook(long id, string bookName, string? www)
    {
        return new DomainAudiobook(
            new List<Person>(), bookName, 2024, new AudiobookFileInfo($"/library/{bookName}.m4b", $"{bookName}.m4b", 1000))
        {
            Id = id,
            Www = www,
        };
    }

    [TestMethod]
    public async Task FindDirtyUrlsAsync_FlagsBooksWhoseUrlHasTrackingParameters()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=a_search"),
            MakeDbAudiobook(2, "Book Two", "https://hardcover.app/books/connections"),
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindDirtyUrlsAsync();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(1, results[0].AudiobookId);
        Assert.AreEqual("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=a_search", results[0].CurrentUrl);
        Assert.AreEqual("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8", results[0].CleanedUrl);
    }

    [TestMethod]
    public async Task FindDirtyUrlsAsync_IgnoresBooksWithNoUrl()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", null),
            MakeDbAudiobook(2, "Book Two", "   "),
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindDirtyUrlsAsync();

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task ApplyAsync_UpdatesOnlyRequestedDirtyBooksThroughAudiobookService()
    {
        _audiobookService.Setup(s => s.GetAudiobookById(1))
            .ReturnsAsync(MakeDomainAudiobook(1, "Book One", "https://www.audible.com/pd/B07NZY2WT8?qid=123"));
        _audiobookService.Setup(s => s.GetAudiobookById(2))
            .ReturnsAsync(MakeDomainAudiobook(2, "Book Two", "https://www.goodreads.com/book/show/1?ref=x"));

        var updated = await _service.ApplyAsync(new long[] { 1 });

        Assert.AreEqual(1, updated);
        _audiobookService.Verify(
            s => s.UpdateAudiobook(1, It.Is<DomainAudiobook>(a => a.Www == "https://www.audible.com/pd/B07NZY2WT8"), null),
            Times.Once);
        _audiobookService.Verify(s => s.UpdateAudiobook(2, It.IsAny<DomainAudiobook>(), null), Times.Never);
        // FindDirtyUrlsAsync's full-library scan must not be re-run just to apply a caller-given id set.
        _audiobookRepository.Verify(r => r.GetAllWithIncludesAsync(), Times.Never);
    }

    [TestMethod]
    public async Task ApplyAsync_SkipsBookThatIsAlreadyClean()
    {
        _audiobookService.Setup(s => s.GetAudiobookById(1))
            .ReturnsAsync(MakeDomainAudiobook(1, "Book One", "https://hardcover.app/books/connections"));

        var updated = await _service.ApplyAsync(new long[] { 1 });

        Assert.AreEqual(0, updated);
        _audiobookService.Verify(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<DomainAudiobook>(), null), Times.Never);
    }

    [TestMethod]
    public async Task ApplyAsync_SkipsBookCurrentlyHeldByAnotherOperation()
    {
        _audiobookService.Setup(s => s.GetAudiobookById(1))
            .ReturnsAsync(MakeDomainAudiobook(1, "Book One", "https://www.audible.com/pd/B07NZY2WT8?qid=123"));

        using (_saveGate.Acquire(1))
        {
            var updated = await _service.ApplyAsync(new long[] { 1 });

            Assert.AreEqual(0, updated);
            _audiobookService.Verify(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<DomainAudiobook>(), null), Times.Never);
        }
    }

    [TestMethod]
    public async Task ApplyAsync_ReturnsZeroAndSkipsLookupWhenNoIdsGiven()
    {
        var updated = await _service.ApplyAsync(Array.Empty<long>());

        Assert.AreEqual(0, updated);
        _audiobookService.Verify(s => s.GetAudiobookById(It.IsAny<long>()), Times.Never);
    }
}
