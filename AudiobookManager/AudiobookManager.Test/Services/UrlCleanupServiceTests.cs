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

    private static DirtyUrlRow MakeDirtyUrlRow(long id, string bookName, string www, List<string>? authors = null)
    {
        return new DirtyUrlRow(id, bookName, authors ?? new List<string>(), www);
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
    public async Task FindDirtyUrlsPageAsync_FlagsBooksWhoseUrlHasTrackingParameters()
    {
        var rows = new List<DirtyUrlRow>
        {
            MakeDirtyUrlRow(1, "Book One", "https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=a_search", ["Author A"]),
            MakeDirtyUrlRow(2, "Book Two", "https://www.audible.com/pd/Book-Two?pf_rd_r=ABC#pageLoadId"),
        };
        _audiobookRepository.Setup(r => r.GetDirtyUrlPageAsync(50, 0)).ReturnsAsync((rows, 2));

        var (results, total) = await _service.FindDirtyUrlsPageAsync(0, 50);

        Assert.AreEqual(2, total, "The total is the whole matching set, not the page.");
        Assert.AreEqual(2, results.Count);
        Assert.AreEqual(1, results[0].AudiobookId);
        Assert.AreSequenceEqual(["Author A"], results[0].Authors);
        Assert.AreEqual("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8?qid=123&ref=a_search", results[0].CurrentUrl);
        Assert.AreEqual("https://www.audible.com/pd/Winter-Dark-Audiobook/B07NZY2WT8", results[0].CleanedUrl);
        Assert.AreEqual("https://www.audible.com/pd/Book-Two", results[1].CleanedUrl);
    }

    [TestMethod]
    public async Task FindDirtyUrlsPageAsync_PassesThePageThroughToTheRepository()
    {
        _audiobookRepository.Setup(r => r.GetDirtyUrlPageAsync(25, 100)).ReturnsAsync((new List<DirtyUrlRow>(), 0));

        var (results, total) = await _service.FindDirtyUrlsPageAsync(4, 25);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, total);
        _audiobookRepository.Verify(r => r.GetDirtyUrlPageAsync(25, 100), Times.Once);
    }

    [TestMethod]
    public async Task CountDirtyUrlsAsync_DelegatesToTheRepository()
    {
        _audiobookRepository.Setup(r => r.CountDirtyUrlsAsync()).ReturnsAsync(2500);

        var count = await _service.CountDirtyUrlsAsync();

        Assert.AreEqual(2500, count);
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
        // The list scan must not be re-run just to apply a caller-given id set.
        _audiobookRepository.Verify(r => r.GetDirtyUrlPageAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
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
