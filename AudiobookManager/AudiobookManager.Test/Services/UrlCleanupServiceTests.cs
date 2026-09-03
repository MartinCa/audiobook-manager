using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DbPerson = AudiobookManager.Database.Models.Person;

namespace AudiobookManager.Test.Services;

[TestClass]
public class UrlCleanupServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private UrlCleanupService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _service = new UrlCleanupService(_audiobookRepository.Object);
    }

    private static DbAudiobook MakeDbAudiobook(long id, string bookName, string? www, List<DbPerson>? authors = null)
    {
        return new DbAudiobook(id, bookName, null, null, null, 2024, null, null, null, null, null, null, www,
            null, null, $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = authors ?? new List<DbPerson>()
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
    public async Task ApplyAsync_UpdatesOnlyRequestedDirtyBooks()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", "https://www.audible.com/pd/B07NZY2WT8?qid=123"),
            MakeDbAudiobook(2, "Book Two", "https://www.goodreads.com/book/show/1?ref=x"),
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var updated = await _service.ApplyAsync(new long[] { 1 });

        Assert.AreEqual(1, updated);
        _audiobookRepository.Verify(r => r.UpdateWwwAsync(1, "https://www.audible.com/pd/B07NZY2WT8"), Times.Once);
        _audiobookRepository.Verify(r => r.UpdateWwwAsync(2, It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ApplyAsync_ReturnsZeroAndSkipsLookupWhenNoIdsGiven()
    {
        var updated = await _service.ApplyAsync(Array.Empty<long>());

        Assert.AreEqual(0, updated);
        _audiobookRepository.Verify(r => r.GetAllWithIncludesAsync(), Times.Never);
    }
}
