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
    public async Task ResolveAsync_RenamesThePersonOnEveryBookCarryingIt_AndClearsAllTheirIssues()
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

        var (scope, result) = await _resolver.ResolveAsync(Issue(42, "J. K. Rowling", "J.K. Rowling"));

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

        // Every book's issues are cleared, since the rewrite invalidates per-book checks.
        foreach (var id in new[] { 1L, 2L, 3L })
        {
            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(id), Times.Once);
        }

        Assert.AreEqual(ResolveScope.AllForAudiobook, scope);
        Assert.AreEqual("resolved", result.ActionTaken);
        StringAssert.Contains(result.Message, "3 books");
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

        await _resolver.ResolveAsync(Issue(43, "S. A. Chakraborty", "S.A. Chakraborty"));

        _audiobookService.Verify(
            s => s.UpdateAudiobook(4, It.Is<DomainAudiobook>(
                a => a.Narrators.Any(p => p.Name == "S.A. Chakraborty")
                     && a.Authors.All(p => p.Name != "S. A. Chakraborty"))),
            Times.Once);
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

        // The failed book keeps its issues (it will be re-flagged); the succeeded one's are cleared.
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(1), Times.Never);
        _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(2), Times.Once);

        Assert.AreEqual(ResolveScope.AllForAudiobook, scope);
        StringAssert.Contains(result.Message, "1 failed");
    }

    [TestMethod]
    public async Task ResolveAsync_PersonNoLongerOnAnyBook_IsTreatedAsStaleAndCleared()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksByPersonNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook>());

        var (_, result) = await _resolver.ResolveAsync(Issue(45, "Gone Person", "Gone P."));

        _issueRepository.Verify(r => r.DeleteAsync(45), Times.Once);
        Assert.AreEqual("resolved", result.ActionTaken);
    }
}