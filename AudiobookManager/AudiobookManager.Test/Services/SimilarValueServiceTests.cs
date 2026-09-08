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
    private AudiobookSaveGate _saveGate = null!;
    private SimilarValueDetectionCache _detectionCache = null!;
    private SimilarValueService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _personRepository = new Mock<IPersonRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        _logger = new Mock<ILogger<SimilarValueService>>();
        _saveGate = new AudiobookSaveGate();
        _detectionCache = new SimilarValueDetectionCache();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookImportPath = "/import",
            AudiobookLibraryPath = "/library"
        });

        _service = new SimilarValueService(
            _audiobookRepository.Object,
            _personRepository.Object,
            _audiobookService.Object,
            _saveGate,
            _detectionCache,
            _settings,
            _logger.Object);
    }

    private static DbAudiobook MakeDbAudiobook(long id, string bookName, string? series = null)
    {
        return new DbAudiobook(id, bookName, null, series, null, 2024, null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);
    }

    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_GroupsNearDuplicateAuthorNames()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "J.K. Rowling", "JK Rowling", "Brandon Sanderson",
        });
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int> { ["J.K. Rowling"] = 1, ["JK Rowling"] = 2 });

        var (groups, total) = await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        Assert.AreEqual(1, total);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(2, groups[0].Candidates.Count);
        var rowlingCandidate = groups[0].Candidates.First(c => c.Value == "J.K. Rowling");
        Assert.AreEqual(1, rowlingCandidate.BookCount);
    }

    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_PagesTheGroupsAndReportsTheFullTotal()
    {
        // Three near-duplicate pairs (punctuation-folded equal, single-edit-distance in the long
        // bucket, punctuation-folded equal) = three groups, so slicing by page actually slices.
        // A lone name forms no cluster - the grouper only returns groups with more than one
        // member - so every name below must be part of a pair.
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "J.K. Rowling", "JK Rowling",
            "Brandon Sanderson", "Brandan Sanderson",
            "Marcel Proust", "Marcel.Proust",
        });
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());

        var (firstPage, total) = await _service.DetectSimilarAuthorsAsync(skip: 0, take: 2);
        var (secondPage, _) = await _service.DetectSimilarAuthorsAsync(skip: 2, take: 2);

        Assert.AreEqual(3, total, "The total is the number of groups, not the slice.");
        Assert.AreEqual(2, firstPage.Count);
        Assert.AreEqual(1, secondPage.Count);
        var seen = firstPage.SelectMany(g => g.Candidates).Select(c => c.Value)
            .Concat(secondPage.SelectMany(g => g.Candidates).Select(c => c.Value))
            .ToHashSet();
        Assert.AreEqual(6, seen.Count, "No candidate may appear on two pages; the group order has to be deterministic.");
    }

    // The point of the cache: paging through the results must not re-read the distinct values and
    // re-cluster the whole library per request. Two paged requests share one clustering run.
    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_ServesRepeatedPagesFromTheCache()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "J.K. Rowling", "JK Rowling",
        });
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());

        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);
        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        _personRepository.Verify(r => r.GetAuthorNamesAsync(), Times.Once,
            "the second request must be served from the cached grouping");
    }

    // Book counts are the only number the page renders per candidate, so they are fetched only
    // for the candidates the returned page shows - never for every value in the library.
    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_FetchesCountsOnlyForTheReturnedPage()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "J.K. Rowling", "JK Rowling",
            "Brandon Sanderson", "Brandan Sanderson",
            "Marcel Proust", "Marcel.Proust",
        });

        IReadOnlyCollection<string>? countedValues = null;
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<IReadOnlyCollection<string>>(values => countedValues = values)
            .ReturnsAsync(new Dictionary<string, int>());

        // Groups sort by their first candidate: the Sanderson pair is alphabetically first.
        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 1);

        Assert.IsNotNull(countedValues);
        Assert.AreEqual(2, countedValues!.Count, "only the returned group's candidates are counted");
        CollectionAssert.AreEquivalent(
            new List<string> { "Brandon Sanderson", "Brandan Sanderson" }, countedValues!.ToList());
    }

    // Alignment folds two values together, so the cached grouping (which still lists both) must
    // not survive it - otherwise the page would go on showing a group the user just merged.
    [TestMethod]
    public async Task AlignSeriesAsync_InvalidatesTheDetectionCache()
    {
        _audiobookRepository.Setup(r => r.GetSeriesNamesAsync()).ReturnsAsync(new List<string>
        {
            "Fantasy & Adventure", "Fantasy and Adventure",
        });
        _audiobookRepository.Setup(r => r.GetSeriesBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());
        _audiobookRepository.Setup(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook>());

        await _service.DetectSimilarSeriesAsync(skip: 0, take: 50);

        await _service.AlignSeriesAsync(
            new List<string> { "Fantasy & Adventure", "Fantasy and Adventure" },
            "Fantasy & Adventure",
            (_, _, _, _) => Task.CompletedTask);

        await _service.DetectSimilarSeriesAsync(skip: 0, take: 50);

        _audiobookRepository.Verify(r => r.GetSeriesNamesAsync(), Times.Exactly(2),
            "alignment must invalidate the cached grouping so the merged value is re-detected");
    }

    // Regression for the stale-publication race: a request that missed the cache and is still
    // reading the distinct values when an alignment invalidates must NOT publish its
    // pre-alignment groups back into the cache for the TTL. The test coordinates the interleaving
    // with tasks (the read blocks on a gate until the invalidate has run), so it is deterministic
    // and has no sleeps: the in-flight compute started against the pre-merge library, its publish
    // must be dropped, and the next request must re-read rather than be served the stale groups.
    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_InvalidationDuringAnInFlightMiss_DoesNotPublishStaleGroups()
    {
        var readStarted = new TaskCompletionSource();
        var releaseRead = new TaskCompletionSource();
        var reads = 0;

        _personRepository.Setup(r => r.GetAuthorNamesAsync()).Returns(async () =>
        {
            reads++;
            if (reads == 1)
            {
                // The first request reads the pre-alignment library: the two-spelling pair.
                readStarted.SetResult();
                await releaseRead.Task;
                return new List<string> { "J.K. Rowling", "JK Rowling" };
            }

            // Post-alignment: the two spellings have been merged into one, so no group remains.
            return new List<string>();
        });
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());

        // Call 1 misses the cache and blocks mid-read on the pre-alignment data.
        var inFlight = _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);
        await readStarted.Task;

        // The alignment commits and invalidates while the compute is still reading.
        _detectionCache.Invalidate();
        releaseRead.SetResult();

        var preAlignmentResult = await inFlight;
        Assert.AreEqual(1, preAlignmentResult.Total,
            "the in-flight request still returns the snapshot it read; the cache is where staleness is refused");

        Assert.IsNull(_detectionCache.Get("authors"),
            "the pre-alignment groups must not be republished after the invalidation");

        // Call 2 must not be served the dropped (pre-alignment) grouping - it has to re-read.
        var fresh = await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);
        Assert.AreEqual(0, fresh.Total, "the re-read sees the merged single value, so nothing clusters");
        _personRepository.Verify(r => r.GetAuthorNamesAsync(), Times.Exactly(2),
            "call 2 must recompute - the stale publish was refused");
    }

    [TestMethod]
    public async Task DetectSimilarSeriesAsync_GroupsNearDuplicateSeriesValues()
    {
        _audiobookRepository.Setup(r => r.GetSeriesNamesAsync()).ReturnsAsync(new List<string>
        {
            "Fantasy & Adventure", "Fantasy and Adventure", "Mystery",
        });
        _audiobookRepository.Setup(r => r.GetSeriesBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int> { ["Fantasy & Adventure"] = 2, ["Fantasy and Adventure"] = 1 });

        var (groups, total) = await _service.DetectSimilarSeriesAsync(skip: 0, take: 50);

        Assert.AreEqual(1, total);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(2, groups[0].Candidates.Count);
        Assert.AreEqual(2, groups[0].Candidates.First(c => c.Value == "Fantasy & Adventure").BookCount);
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
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

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

    // Regression: alignment rewrites m4b tags and can relocate files, exactly like an interactive
    // save, but the two were gated separately - the save endpoint held a private set of its own -
    // so an alignment and a save could both be rewriting the same book at once. Both now take the
    // one per-audiobook gate, and a book someone is already saving fails just its own item.
    [TestMethod]
    public async Task AlignAuthorsAsync_BookAlreadyBeingSaved_IsCountedAsFailedWithoutTouchingIt()
    {
        var busy = MakeDbAudiobook(4001, "Busy Book");
        busy.Authors = new List<DbPerson> { new(1, "JK Rowling") };
        var free = MakeDbAudiobook(4002, "Free Book");
        free.Authors = new List<DbPerson> { new(1, "JK Rowling") };

        _audiobookRepository.Setup(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { busy, free });
        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);

        // Someone is saving "Busy Book" right now.
        using var lease = _saveGate.Acquire(4001);

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();

        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling", "JK Rowling" },
            "J.K. Rowling",
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        _audiobookService.Verify(s => s.UpdateAudiobook(4001, It.IsAny<Audiobook>()), Times.Never);
        _audiobookService.Verify(s => s.UpdateAudiobook(4002, It.IsAny<Audiobook>()), Times.Once);

        var last = progressCalls.Last();
        Assert.AreEqual(2, last.processed);
        Assert.AreEqual(1, last.succeeded);
        Assert.AreEqual(1, last.failed);
    }

    [TestMethod]
    public async Task AlignSeriesAsync_BookAlreadyBeingSaved_IsCountedAsFailedWithoutTouchingIt()
    {
        var busy = MakeDbAudiobook(4011, "Busy Book", "Old Series");
        var free = MakeDbAudiobook(4012, "Free Book", "Old Series");

        _audiobookRepository.Setup(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { busy, free });
        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);

        using var lease = _saveGate.Acquire(4011);

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();

        await _service.AlignSeriesAsync(
            new List<string> { "Old Series", "New Series" },
            "New Series",
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        _audiobookService.Verify(s => s.UpdateAudiobook(4011, It.IsAny<Audiobook>()), Times.Never);
        _audiobookService.Verify(s => s.UpdateAudiobook(4012, It.IsAny<Audiobook>()), Times.Once);

        var last = progressCalls.Last();
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
            .Callback<long, Audiobook, Func<string, int, Task>?>((id, a, _) => capturedAudiobook = a)
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

        await _service.AlignAuthorsAsync(
            new List<string> { "JK Rowling", "J.K. Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        Assert.IsNotNull(capturedAudiobook);
        Assert.AreEqual(1, capturedAudiobook!.Authors.Count);
        Assert.AreEqual("J.K. Rowling", capturedAudiobook.Authors[0].Name);
    }

    [TestMethod]
    public async Task AlignAuthorsAsync_BookHasBothTargetAndSourceAuthor_TargetIsNotDuplicated()
    {
        // Book already lists the target author name literally, plus a source (to-be-merged) name
        // as a separate author. The target must appear exactly once after alignment.
        var book = MakeDbAudiobook(1, "Book One");
        book.Authors = new List<DbPerson> { new(1, "J.K. Rowling"), new(2, "JK Rowling") };

        _audiobookRepository.Setup(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book });

        Audiobook? capturedAudiobook = null;
        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()))
            .Callback<long, Audiobook, Func<string, int, Task>?>((id, a, _) => capturedAudiobook = a)
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling", "JK Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        Assert.IsNotNull(capturedAudiobook);
        Assert.AreEqual(1, capturedAudiobook!.Authors.Count);
        Assert.AreEqual("J.K. Rowling", capturedAudiobook.Authors[0].Name);
    }

    [TestMethod]
    public async Task AlignAuthorsAsync_ExcludesTargetNameFromBookLookup()
    {
        var book = MakeDbAudiobook(1, "Book One");
        book.Authors = new List<DbPerson> { new(2, "JK Rowling") };

        IEnumerable<string>? queriedNames = null;
        _audiobookRepository.Setup(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(names => queriedNames = names)
            .ReturnsAsync(new List<DbAudiobook> { book });

        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling", "JK Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        CollectionAssert.AreEquivalent(new List<string> { "JK Rowling" }, queriedNames!.ToList());
    }

    [TestMethod]
    public async Task AlignAuthorsAsync_OnlyTargetNameInGroup_DoesNothing()
    {
        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();

        var result = await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling" },
            "J.K. Rowling",
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual((0, 0, 0), result);
        Assert.AreEqual(0, progressCalls.Count);
        _audiobookRepository.Verify(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        _audiobookService.Verify(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task AlignSeriesAsync_ExcludesTargetValueFromBookLookup()
    {
        var book = MakeDbAudiobook(2, "Book Two", "Fantasy and Adventure");

        IEnumerable<string>? queriedValues = null;
        _audiobookRepository.Setup(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()))
            .Callback<IEnumerable<string>>(values => queriedValues = values)
            .ReturnsAsync(new List<DbAudiobook> { book });

        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

        await _service.AlignSeriesAsync(
            new List<string> { "Fantasy & Adventure", "Fantasy and Adventure" },
            "Fantasy & Adventure",
            (_, _, _, _) => Task.CompletedTask);

        CollectionAssert.AreEquivalent(new List<string> { "Fantasy and Adventure" }, queriedValues!.ToList());
    }

    [TestMethod]
    public async Task AlignSeriesAsync_OnlyTargetValueInGroup_DoesNothing()
    {
        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();

        var result = await _service.AlignSeriesAsync(
            new List<string> { "Fantasy & Adventure" },
            "Fantasy & Adventure",
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual((0, 0, 0), result);
        Assert.AreEqual(0, progressCalls.Count);
        _audiobookRepository.Verify(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        _audiobookService.Verify(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task AlignSeriesAsync_UpdatesSeriesForAllAffectedBooks()
    {
        var book1 = MakeDbAudiobook(1, "Book One", "Fantasy & Adventure");
        var book2 = MakeDbAudiobook(2, "Book Two", "Fantasy and Adventure");

        _audiobookRepository.Setup(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook> { book1, book2 });

        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

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
