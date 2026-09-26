using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
using DomainMetadataRefreshResult = AudiobookManager.Domain.MetadataRefreshResult;

namespace AudiobookManager.Test.Services;

[TestClass]
public class PendingOnlineMatchServiceTests
{
    private readonly Mock<IAudiobookRepository> _audiobookRepository = new();
    private readonly Mock<IPendingOnlineMatchRepository> _repository = new();
    private readonly Mock<IScrapingService> _scrapingService = new();
    private readonly Mock<IMetadataRefreshService> _metadataRefreshService = new();
    private readonly Mock<ILogger<PendingOnlineMatchService>> _logger = new();

    private PendingOnlineMatchService CreateService() =>
        new(
            _audiobookRepository.Object,
            _repository.Object,
            _scrapingService.Object,
            _metadataRefreshService.Object,
            _logger.Object);

    private static Audiobook Book(long id, string? bookName, string fileName, params string[] authorNames)
    {
        var book = new Audiobook(
            id, bookName ?? string.Empty, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{fileName}", fileName, 1000);
        book.Authors = authorNames.Select(name => new Person(0, name)).ToList();
        return book;
    }

    private static readonly Func<int, int, int, int, Task> NoopProgress = (_, _, _, _) => Task.CompletedTask;

    #region SearchSelectedAsync

    [TestMethod]
    public async Task SearchSelectedAsync_BookHasATitle_SearchesWithBookNameNotFileName()
    {
        var book = Book(1, "The Real Title", "some-file.m4b");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { book });
        _scrapingService.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "The Real Title"))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        var result = await CreateService().SearchSelectedAsync(
            new List<long> { 1 }, new List<string> { "Audible" }, NoopProgress);

        Assert.AreEqual(1, result.Succeeded);
        _scrapingService.Verify(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "The Real Title"), Times.Once);
        _scrapingService.Verify(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "some-file.m4b"), Times.Never);
    }

    [TestMethod]
    public async Task SearchSelectedAsync_BookHasAuthorAndTitle_SearchesWithAuthorDashTitle()
    {
        var book = Book(4, "The Real Title", "some-file.m4b", "Brandon Sanderson");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { book });
        _scrapingService.Setup(s => s.SearchMultiple(
                It.IsAny<IEnumerable<string>>(), "Brandon Sanderson - The Real Title"))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        var result = await CreateService().SearchSelectedAsync(
            new List<long> { 4 }, new List<string> { "Audible" }, NoopProgress);

        Assert.AreEqual(1, result.Succeeded);
        _scrapingService.Verify(
            s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "Brandon Sanderson - The Real Title"),
            Times.Once);
    }

    [TestMethod]
    public async Task SearchSelectedAsync_BookHasNoTitle_FallsBackToFileName()
    {
        var book = Book(2, null, "book-two.m4b");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { book });
        _scrapingService.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "book-two.m4b"))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        var result = await CreateService().SearchSelectedAsync(
            new List<long> { 2 }, new List<string> { "Audible" }, NoopProgress);

        Assert.AreEqual(1, result.Succeeded);
        _scrapingService.Verify(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "book-two.m4b"), Times.Once);
    }

    [TestMethod]
    public async Task SearchSelectedAsync_OneBookFailsToResolve_OthersStillSucceed_PerItemFailureIsTolerated()
    {
        var found = Book(10, "Found Book", "found.m4b");
        // Id 11 is requested but not returned by the repository - simulates a stale/removed id.
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { found });
        _scrapingService.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "Found Book"))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        var result = await CreateService().SearchSelectedAsync(
            new List<long> { 10, 11 }, new List<string> { "Audible" }, NoopProgress);

        Assert.AreEqual(2, result.Processed);
        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(1, result.Succeeded);
        Assert.AreEqual(1, result.Failed);
        _repository.Verify(
            r => r.UpsertAsync(It.Is<PendingOnlineMatch>(m => m.AudiobookId == 10)), Times.Once);
        _repository.Verify(
            r => r.UpsertAsync(It.Is<PendingOnlineMatch>(m => m.AudiobookId == 11)), Times.Never);
    }

    [TestMethod]
    public async Task SearchSelectedAsync_BookHasNoTitleOrFileName_CountsFailedAndSkipsTheSearch()
    {
        var book = Book(3, null, string.Empty);
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { book });

        var result = await CreateService().SearchSelectedAsync(
            new List<long> { 3 }, new List<string> { "Audible" }, NoopProgress);

        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(0, result.Succeeded);
        _scrapingService.Verify(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()), Times.Never);
    }

    // A fresh search must supersede whatever status the book's row previously had (e.g. Rejected)
    // - the user just explicitly asked for it to be searched again.
    [TestMethod]
    public async Task SearchSelectedAsync_UpsertsWithStatusPending_SupersedingAnyPriorRejection()
    {
        var book = Book(20, "A Book", "a-book.m4b");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { book });
        var searchResult = new MetadataMultiSourceSearchResult
        {
            Results = new List<MetadataSearchResult>
            {
                new("https://example.com/a-book", "A Book") { Source = "Audible" },
            },
        };
        _scrapingService.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "A Book"))
            .ReturnsAsync(searchResult);

        await CreateService().SearchSelectedAsync(
            new List<long> { 20 }, new List<string> { "Audible" }, NoopProgress);

        _repository.Verify(
            r => r.UpsertAsync(It.Is<PendingOnlineMatch>(m =>
                m.AudiobookId == 20 &&
                m.Status == PendingOnlineMatchStatus.Pending &&
                m.SourceNamesJson.Contains("Audible"))),
            Times.Once);
    }

    [TestMethod]
    public async Task SearchSelectedAsync_ReportsProgressForEachProcessedBook()
    {
        var book = Book(30, "A Book", "a-book.m4b");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<Audiobook> { book });
        _scrapingService.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "A Book"))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();
        await CreateService().SearchSelectedAsync(
            new List<long> { 30 },
            new List<string> { "Audible" },
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual(1, progressCalls.Count);
        Assert.AreEqual((1, 1, 1, 0), progressCalls[0]);
    }

    #endregion

    #region SelectResultAsync

    private static PendingOnlineMatch RowWithOneCandidate(long audiobookId, string url = "https://example.com/candidate") =>
        new()
        {
            AudiobookId = audiobookId,
            SearchedAt = DateTime.UtcNow,
            Status = PendingOnlineMatchStatus.Pending,
            SourceNamesJson = "[\"Audible\"]",
            ResultsJson = PendingOnlineMatchPayload.Serialize(new List<PendingRefreshPayload.Snapshot>
            {
                new(
                    PendingRefreshPayload.CurrentVersion,
                    url,
                    "Audible",
                    new List<string> { "Author A" },
                    new List<string>(),
                    "A Book",
                    null, null, null, null,
                    new List<string>(),
                    null, null, null, null, null, null),
            }),
        };

    [TestMethod]
    public async Task SelectResultAsync_HappyPath_FetchesDetailsThenAppliesThenDeletesTheRow()
    {
        var row = RowWithOneCandidate(50);
        _repository.Setup(r => r.GetByAudiobookIdAsync(50)).ReturnsAsync(row);
        var fetched = new MetadataSearchResult("https://example.com/candidate", "A Book") { Source = "Audible" };
        _scrapingService.Setup(s => s.GetBookDetails("https://example.com/candidate")).ReturnsAsync(fetched);
        _metadataRefreshService
            .Setup(s => s.ApplyFetchedResultAsSnapshotAsync(50, fetched))
            .ReturnsAsync(new DomainMetadataRefreshResult { Success = true });
        _repository.Setup(r => r.DeleteByAudiobookIdAsync(50)).ReturnsAsync(true);

        await CreateService().SelectResultAsync(50, 0);

        _scrapingService.Verify(s => s.GetBookDetails("https://example.com/candidate"), Times.Once);
        _metadataRefreshService.Verify(s => s.ApplyFetchedResultAsSnapshotAsync(50, fetched), Times.Once);
        _repository.Verify(r => r.DeleteByAudiobookIdAsync(50), Times.Once);
    }

    [TestMethod]
    public async Task SelectResultAsync_NoRowForThisBook_ThrowsKeyNotFoundException()
    {
        _repository.Setup(r => r.GetByAudiobookIdAsync(60)).ReturnsAsync((PendingOnlineMatch?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            () => CreateService().SelectResultAsync(60, 0));

        _scrapingService.Verify(s => s.GetBookDetails(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task SelectResultAsync_IndexBeyondStoredCandidates_ThrowsArgumentOutOfRangeException()
    {
        var row = RowWithOneCandidate(70);
        _repository.Setup(r => r.GetByAudiobookIdAsync(70)).ReturnsAsync(row);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => CreateService().SelectResultAsync(70, 5));

        _scrapingService.Verify(s => s.GetBookDetails(It.IsAny<string>()), Times.Never);
        _repository.Verify(r => r.DeleteByAudiobookIdAsync(It.IsAny<long>()), Times.Never);
    }

    [TestMethod]
    public async Task SelectResultAsync_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var row = RowWithOneCandidate(71);
        _repository.Setup(r => r.GetByAudiobookIdAsync(71)).ReturnsAsync(row);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => CreateService().SelectResultAsync(71, -1));
    }

    #endregion

    #region RejectAsync / DismissAsync

    [TestMethod]
    public async Task RejectAsync_DelegatesToRepositoryWithRejectedStatus()
    {
        _repository.Setup(r => r.SetStatusAsync(80, PendingOnlineMatchStatus.Rejected)).ReturnsAsync(true);

        var result = await CreateService().RejectAsync(80);

        Assert.IsTrue(result);
        _repository.Verify(r => r.SetStatusAsync(80, PendingOnlineMatchStatus.Rejected), Times.Once);
    }

    [TestMethod]
    public async Task RejectAsync_NoRowForThisBook_ReturnsFalse()
    {
        _repository.Setup(r => r.SetStatusAsync(81, PendingOnlineMatchStatus.Rejected)).ReturnsAsync(false);

        var result = await CreateService().RejectAsync(81);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task DismissAsync_DelegatesToRepositoryDelete()
    {
        _repository.Setup(r => r.DeleteByAudiobookIdAsync(90)).ReturnsAsync(true);

        var result = await CreateService().DismissAsync(90);

        Assert.IsTrue(result);
        _repository.Verify(r => r.DeleteByAudiobookIdAsync(90), Times.Once);
    }

    [TestMethod]
    public async Task DismissAsync_NoRowForThisBook_ReturnsFalse()
    {
        _repository.Setup(r => r.DeleteByAudiobookIdAsync(91)).ReturnsAsync(false);

        var result = await CreateService().DismissAsync(91);

        Assert.IsFalse(result);
    }

    #endregion

    #region GetPageAsync

    [TestMethod]
    public async Task GetPageAsync_DelegatesPagingParametersToTheRepository()
    {
        _repository.Setup(r => r.GetPageWithAudiobookAsync(PendingOnlineMatchStatus.Pending, 20, 10))
            .ReturnsAsync((new List<PendingOnlineMatch>(), 0));

        await CreateService().GetPageAsync(PendingOnlineMatchStatus.Pending, page: 2, pageSize: 10);

        _repository.Verify(
            r => r.GetPageWithAudiobookAsync(PendingOnlineMatchStatus.Pending, 20, 10), Times.Once);
    }

    #endregion
}
