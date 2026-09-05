using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
using DbPerson = AudiobookManager.Database.Models.Person;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;

namespace AudiobookManager.Test.Services.Consistency.Resolvers;

[TestClass]
public class InitialsSpacingResolverTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<IConsistencyIssueRepository> _issueRepository = null!;
    private AudiobookSaveGate _saveGate = null!;
    private InitialsSpacingResolver _resolver = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _saveGate = new AudiobookSaveGate();
        _resolver = new InitialsSpacingResolver(
            _audiobookRepository.Object,
            _audiobookService.Object,
            _issueRepository.Object,
            _saveGate,
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<InitialsSpacingResolver>());
    }

    private static DbAudiobook Book(long id, string bookName, params string[] authorNames) =>
        new(id, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{id}.m4b", $"{id}.m4b", 1000)
        {
            Authors = authorNames.Select(name => new DbPerson(default, name)).ToList()
        };

    private static ConsistencyIssue Issue(long id, string actual, string expected) => new()
    {
        Id = id,
        AudiobookId = 1,
        IssueType = ConsistencyIssueType.InitialsSpacingMismatch,
        Description = "test",
        ActualValue = actual,
        ExpectedValue = expected,
        DetectedAt = DateTime.UtcNow
    };

    [TestMethod]
    public async Task ResolveAsync_RenamesThePersonOnEveryBookCarryingIt_AndClearsOnlyWhatTheRewriteInvalidates()
    {
        // "J. K. Rowling" (spaced) on three books; under Unspaced the canonical is "J.K. Rowling".
        var book1 = Book(1, "Book One", "J. K. Rowling");
        var book2 = Book(2, "Book Two", "J. K. Rowling");
        var book3 = Book(3, "Book Three", "J. K. Rowling", "Brandon Sanderson");

        _audiobookRepository
            .Setup(r => r.GetBooksByPersonNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book1, book2, book3 });

        _audiobookService
            .Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<DomainAudiobook>()))
            .ReturnsAsync((long id, DomainAudiobook a, Func<string, int, Task>? _) => a);

        var issue = Issue(42, "J. K. Rowling", "J.K. Rowling");
        var (scope, result) = await _resolver.ResolveAsync(issue);

        // The old spelling is replaced everywhere it appears on any authored book.
        foreach (var id in new[] { 1L, 2L, 3L })
        {
            _audiobookService.Verify(
                s => s.UpdateAudiobook(id, It.Is<DomainAudiobook>(
                    a => a.Authors.Any(p => p.Name == "J.K. Rowling")
                         && a.Authors.All(p => p.Name != "J. K. Rowling"))),
                Times.Once);
        }

        // Brandon Sanderson on book 3 is untouched.
        _audiobookService.Verify(
            s => s.UpdateAudiobook(3, It.Is<DomainAudiobook>(
                a => a.Authors.Any(p => p.Name == "Brandon Sanderson"))),
            Times.Once);

        // Each rewritten book's stale per-book rows are cleared - but never InitialsSpacingMismatch
        // rows for other persons, which this person-scoped rewrite says nothing about.
        foreach (var id in new[] { 1L, 2L, 3L })
        {
            _issueRepository.Verify(
                r => r.DeleteByAudiobookIdAndTypesAsync(id,
                    It.Is<IEnumerable<ConsistencyIssueType>>(types =>
                        types.Contains(ConsistencyIssueType.TagMismatch)
                        && !types.Contains(ConsistencyIssueType.InitialsSpacingMismatch))),
                Times.Once);
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(id), Times.Never);
        }

        // The resolved person's own row is cleared explicitly once the rename fully succeeded.
        _issueRepository.Verify(r => r.DeleteAsync(42), Times.Once);

        // IssueOnly: the resolve never settles another person's issue on the same representative book.
        Assert.AreEqual(ResolveScope.IssueOnly, scope);
        Assert.AreEqual("resolved", result.ActionTaken);
        StringAssert.Contains(result.Message, "3 books");
    }

    // Regression test for the review finding: a book can be the representative for two different
    // non-compliant persons. Resolving the Rowling issue rewrites the book (its per-book rows are
    // stale and cleared), but the Chakraborty InitialsSpacingMismatch row - which points at the same
    // book but was never named by this resolve - must survive. The blanket delete-by-book used to
    // remove it, making the UI show Chakraborty as resolved while her name still does not comply.
    [TestMethod]
    public async Task ResolveAsync_DoesNotDeleteAnotherPersonsInitialsSpacingIssueOnTheSharedRepresentativeBook()
    {
        var sharedBook = Book(1, "Shared Book", "J. K. Rowling");
        sharedBook.Narrators = new List<DbPerson> { new(default, "S. A. Chakraborty") };

        _audiobookRepository
            .Setup(r => r.GetBooksByPersonNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { sharedBook });
        _audiobookService
            .Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<DomainAudiobook>()))
            .ReturnsAsync((long id, DomainAudiobook a, Func<string, int, Task>? _) => a);

        var (scope, result) = await _resolver.ResolveAsync(Issue(42, "J. K. Rowling", "J.K. Rowling"));

        // The shared book's InitialsSpacingMismatch rows are never even enumerated for deletion.
        _issueRepository.Verify(
            r => r.DeleteByAudiobookIdAndTypesAsync(1, It.IsAny<IEnumerable<ConsistencyIssueType>>()),
            Times.Once);
        _issueRepository.Verify(
            r => r.DeleteByAudiobookIdAndTypesAsync(1,
                It.Is<IEnumerable<ConsistencyIssueType>>(types => types.Contains(ConsistencyIssueType.InitialsSpacingMismatch))),
            Times.Never);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Never);

        // Only the resolved person's own row is deleted.
        _issueRepository.Verify(r => r.DeleteAsync(42), Times.Once);

        // And the scope is IssueOnly, so a bulk resolve does not skip Chakraborty's issue via the
        // shared representative book's cascadedAll set.
        Assert.AreEqual(ResolveScope.IssueOnly, scope);
        Assert.AreEqual("resolved", result.ActionTaken);
    }

    [TestMethod]
    public async Task ResolveAsync_AlsoRenamesNarrators()
    {
        var narrated = Book(4, "Narrated Book", "Brandon Sanderson");
        narrated.Narrators = new List<DbPerson> { new(default, "S. A. Chakraborty") };

        _audiobookRepository
            .Setup(r => r.GetBooksByPersonNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { narrated });
        _audiobookService
            .Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<DomainAudiobook>()))
            .ReturnsAsync((long id, DomainAudiobook a, Func<string, int, Task>? _) => a);

        var (scope, _) = await _resolver.ResolveAsync(Issue(43, "S. A. Chakraborty", "S.A. Chakraborty"));

        _audiobookService.Verify(
            s => s.UpdateAudiobook(4, It.Is<DomainAudiobook>(
                a => a.Narrators.Any(p => p.Name == "S.A. Chakraborty")
                     && a.Authors.All(p => p.Name != "S. A. Chakraborty"))),
            Times.Once);

        Assert.AreEqual(ResolveScope.IssueOnly, scope);
    }

    [TestMethod]
    public async Task ResolveAsync_OneBookFailsToUpdate_ReportsPartialFailureAndDoesNotClearItsIssues()
    {
        var book1 = Book(1, "First", "J. K. Rowling");
        var book2 = Book(2, "Second", "J. K. Rowling");

        _audiobookRepository
            .Setup(r => r.GetBooksByPersonNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });
        _audiobookService
            .Setup(s => s.UpdateAudiobook(1, It.IsAny<DomainAudiobook>()))
            .ThrowsAsync(new Exception("path collision"));
        _audiobookService
            .Setup(s => s.UpdateAudiobook(2, It.IsAny<DomainAudiobook>()))
            .ReturnsAsync((long id, DomainAudiobook a, Func<string, int, Task>? _) => a);

        var (scope, result) = await _resolver.ResolveAsync(Issue(44, "J. K. Rowling", "J.K. Rowling"));

        // The failed book keeps its issues (it will be re-flagged); the succeeded one's stale
        // per-book rows are cleared (still never InitialsSpacingMismatch rows for other persons).
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Never);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(2), Times.Never);
        _issueRepository.Verify(
            r => r.DeleteByAudiobookIdAndTypesAsync(1, It.IsAny<IEnumerable<ConsistencyIssueType>>()),
            Times.Never);
        _issueRepository.Verify(
            r => r.DeleteByAudiobookIdAndTypesAsync(2, It.IsAny<IEnumerable<ConsistencyIssueType>>()),
            Times.Once);

        // The rename did not fully succeed, so the person still appears on a book: this issue row
        // must survive for the next check to re-flag it.
        _issueRepository.Verify(r => r.DeleteAsync(44), Times.Never);

        Assert.AreEqual(ResolveScope.IssueOnly, scope);
        StringAssert.Contains(result.Message, "1 failed");
    }

    [TestMethod]
    public async Task ResolveAsync_PersonNoLongerOnAnyBook_IsTreatedAsStaleAndCleared()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksByPersonNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook>());

        var (scope, result) = await _resolver.ResolveAsync(Issue(45, "Gone Person", "Gone P."));

        _issueRepository.Verify(r => r.DeleteAsync(45), Times.Once);
        Assert.AreEqual("resolved", result.ActionTaken);
        Assert.AreEqual(ResolveScope.IssueOnly, scope);
    }
}
