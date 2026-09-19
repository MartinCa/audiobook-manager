using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;

namespace AudiobookManager.Test.Services;

/// <summary>
/// <see cref="AuthorReconciliationProvider"/> - the author-detail counterpart of
/// <see cref="SeriesReconciliationProvider"/> (which has no dedicated test class of its own;
/// its matching/classification is exercised indirectly through <c>SeriesServiceTests</c> and
/// <c>LibraryConsistencyServiceTests</c>). Mocks the two repositories directly rather than going
/// through SQLite - the property under test is the reconciliation's own matching/classification
/// logic, not the repositories' queries (which have their own bounded-fetch/overflow tests, see
/// <c>PersonRepositoryTests</c>/<c>AudiobookRepositoryStandaloneOwnedKeysTests</c>).
/// </summary>
[TestClass]
public class AuthorReconciliationProviderTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IPersonRepository> _personRepository = null!;
    private AuthorReconciliationProvider _provider = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _personRepository = new Mock<IPersonRepository>();
        _provider = new AuthorReconciliationProvider(_audiobookRepository.Object, _personRepository.Object);

        _audiobookRepository
            .Setup(r => r.GetStandaloneOwnedKeysByAuthorAsync(It.IsAny<long>(), It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), false));
    }

    private void SetupPerson(long personId, List<AuthorExpectedBook> expectedBooks) =>
        _personRepository
            .Setup(r => r.GetByIdWithExpectedBooksBoundedAsync(personId, It.IsAny<int>()))
            .ReturnsAsync((new Person(personId, "Author") { ExpectedBooks = expectedBooks }, false));

    [TestMethod]
    public async Task GetReconciliationAsync_UnmatchedEntryPastRelease_IsMissing()
    {
        SetupPerson(7, new List<AuthorExpectedBook>
        {
            new() { Id = 1, Title = "Elantris", Year = 2005 },
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("Elantris", result.Missing[0].Title);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(0, result.Ignored.Count);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_UnmatchedEntryWithFutureYear_IsUpcoming()
    {
        SetupPerson(7, new List<AuthorExpectedBook>
        {
            new() { Id = 1, Title = "Future Novel", Year = DateTime.UtcNow.Year + 1 },
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(1, result.Upcoming.Count);
        Assert.AreEqual("Future Novel", result.Upcoming[0].Title);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_EntryMatchingAnOwnedBook_IsNeitherMissingNorUpcoming()
    {
        SetupPerson(7, new List<AuthorExpectedBook>
        {
            new() { Id = 1, Title = "Elantris", Year = 2005 },
        });
        _audiobookRepository
            .Setup(r => r.GetStandaloneOwnedKeysByAuthorAsync(7, It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey> { new(101, null, "Elantris") }, false));

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(1, result.OwnedCount);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_IgnoredEntry_IsExcludedFromMissingAndUpcomingButListedInIgnored()
    {
        SetupPerson(7, new List<AuthorExpectedBook>
        {
            new() { Id = 1, Title = "Elantris", Year = 2005, IsIgnored = true },
            new() { Id = 2, Title = "Warbreaker", Year = 2009 },
        });

        var result = await _provider.GetReconciliationAsync(7);

        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("Warbreaker", result.Missing[0].Title);
        Assert.AreEqual(1, result.Ignored.Count);
        Assert.AreEqual("Elantris", result.Ignored[0].Title);
        Assert.IsTrue(result.Ignored[0].IsIgnored);
        // The ignored entry contributes neither to ExpectedBookCount (which counts only active
        // roster entries) nor to Missing/Upcoming.
        Assert.AreEqual(1, result.ExpectedBookCount);
    }

    [TestMethod]
    public async Task GetReconciliationAsync_RosterOverflow_Throws()
    {
        _personRepository
            .Setup(r => r.GetByIdWithExpectedBooksBoundedAsync(7, It.IsAny<int>()))
            .ReturnsAsync((new Person(7, "Author"), true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _provider.GetReconciliationAsync(7));
    }

    [TestMethod]
    public async Task GetReconciliationAsync_OwnedKeysOverflow_Throws()
    {
        SetupPerson(7, new List<AuthorExpectedBook> { new() { Id = 1, Title = "Elantris" } });
        _audiobookRepository
            .Setup(r => r.GetStandaloneOwnedKeysByAuthorAsync(7, It.IsAny<int>()))
            .ReturnsAsync((new List<SeriesOwnedKey>(), true));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _provider.GetReconciliationAsync(7));
    }

    [TestMethod]
    public async Task GetReconciliationAsync_NoPerson_ReturnsEmptyReconciliation()
    {
        _personRepository
            .Setup(r => r.GetByIdWithExpectedBooksBoundedAsync(999, It.IsAny<int>()))
            .ReturnsAsync(((Person?)null, false));

        var result = await _provider.GetReconciliationAsync(999);

        Assert.AreEqual(0, result.Missing.Count);
        Assert.AreEqual(0, result.Upcoming.Count);
        Assert.AreEqual(0, result.Ignored.Count);
        Assert.AreEqual(0, result.ExpectedBookCount);
    }

    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_ClassifiesEachAuthorSeparately()
    {
        _personRepository
            .Setup(r => r.GetAllActiveAuthorExpectedBooksAsync())
            .ReturnsAsync(new List<AuthorExpectedBookRef>
            {
                new(1, "Elantris", 2005, null), // author 1: missing (past year, not owned)
                new(2, "Warbreaker", DateTime.UtcNow.Year + 1, null), // author 2: upcoming
                new(3, "Mistborn", 2006, null), // author 3: owned, neither
            });
        _audiobookRepository
            .Setup(r => r.GetStandaloneOwnedTitlesByAuthorsAsync(It.IsAny<IReadOnlyCollection<long>>()))
            .ReturnsAsync(new Dictionary<long, List<string>> { [3] = new List<string> { "Mistborn" } });

        var (missing, upcoming) = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        CollectionAssert.AreEquivalent(new long[] { 1 }, missing.ToList());
        CollectionAssert.AreEquivalent(new long[] { 2 }, upcoming.ToList());
    }

    [TestMethod]
    public async Task GetBulkMissingOrUpcomingAuthorIdsAsync_NoActiveExpectedBooks_ReturnsEmptySets()
    {
        _personRepository
            .Setup(r => r.GetAllActiveAuthorExpectedBooksAsync())
            .ReturnsAsync(new List<AuthorExpectedBookRef>());

        var (missing, upcoming) = await _provider.GetBulkMissingOrUpcomingAuthorIdsAsync();

        Assert.AreEqual(0, missing.Count);
        Assert.AreEqual(0, upcoming.Count);
        _audiobookRepository.Verify(
            r => r.GetStandaloneOwnedTitlesByAuthorsAsync(It.IsAny<IReadOnlyCollection<long>>()), Times.Never);
    }
}
