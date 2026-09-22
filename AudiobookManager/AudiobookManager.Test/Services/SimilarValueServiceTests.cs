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
    private Mock<IIgnoredSimilarValuePairRepository> _ignoredPairRepository = null!;
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
        _ignoredPairRepository = new Mock<IIgnoredSimilarValuePairRepository>();
        _ignoredPairRepository.Setup(r => r.GetForKindAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<AudiobookManager.Database.Models.IgnoredSimilarValuePair>());
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
            _ignoredPairRepository.Object,
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

    // Regression for the cache-mutation review finding: pages were carved directly out of the
    // cached detection snapshot, so stamping the per-page book counts wrote through to the very
    // objects the cache holds. The stored graph has to stay a read-only snapshot - a page is a
    // copy, and the copy carries the counts.
    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_BookCountStampingDoesNotMutateTheCachedSnapshot()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "J.K. Rowling", "JK Rowling",
            "Brandon Sanderson", "Brandan Sanderson",
            "Marcel Proust", "Marcel.Proust",
        });

        // The first page (alphabetically the Sanderson group) is served counts.
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int> { ["Brandon Sanderson"] = 9, ["Brandan Sanderson"] = 3 });

        var firstPage = (await _service.DetectSimilarAuthorsAsync(skip: 0, take: 1)).Items;

        Assert.AreEqual(9, firstPage[0].Candidates.First(c => c.Value == "Brandon Sanderson").BookCount,
            "the returned page must carry the freshly read counts");

        var cached = _detectionCache.Get("authors");
        Assert.IsNotNull(cached);
        Assert.IsTrue(
            cached!.SelectMany(g => g.Candidates).All(c => c.BookCount == 0),
            "stamping counts onto a page must not write through to the objects the detection cache holds");
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

    // Regression: an ignored pair naming a value an alignment just rewrote away used to linger
    // forever in "Show ignored" - it can never match a live clustering edge again once the value
    // it names no longer exists, so alignment must sweep it.
    [TestMethod]
    public async Task AlignAuthorsAsync_SweepsIgnoredPairsNamingTheRewrittenSourceNames()
    {
        _audiobookRepository.Setup(r => r.GetBooksByAuthorNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook>());

        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling", "JK Rowling", "J. K. Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        _ignoredPairRepository.Verify(
            r => r.DeleteInvolvingValuesAsync(
                "authors",
                It.Is<IReadOnlyCollection<string>>(v =>
                    v.Count == 2 && v.Contains("JK Rowling") && v.Contains("J. K. Rowling"))),
            Times.Once);
    }

    [TestMethod]
    public async Task AlignSeriesAsync_SweepsIgnoredPairsNamingTheRewrittenSourceValues()
    {
        _audiobookRepository.Setup(r => r.GetBooksBySeriesValuesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<DbAudiobook>());

        await _service.AlignSeriesAsync(
            new List<string> { "Fantasy & Adventure", "Fantasy and Adventure" },
            "Fantasy & Adventure",
            (_, _, _, _) => Task.CompletedTask);

        _ignoredPairRepository.Verify(
            r => r.DeleteInvolvingValuesAsync(
                "series",
                It.Is<IReadOnlyCollection<string>>(v => v.Count == 1 && v.Contains("Fantasy and Adventure"))),
            Times.Once);
    }

    // Alignment that touches nothing (only the target itself in the group) must not sweep either.
    [TestMethod]
    public async Task AlignAuthorsAsync_OnlyTargetNameInGroup_DoesNotSweepIgnoredPairs()
    {
        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        _ignoredPairRepository.Verify(
            r => r.DeleteInvolvingValuesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()),
            Times.Never);
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

    // Regression: a source name is only actually gone from the library if every book carrying it
    // aligned successfully. A failed book (busy save gate, path collision, etc.) still carries the
    // source name, so its ignored pairs are still live and must survive the alignment.
    [TestMethod]
    public async Task AlignAuthorsAsync_OneBookFails_DoesNotSweepIgnoredPairs()
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

        await _service.AlignAuthorsAsync(
            new List<string> { "J.K. Rowling", "JK Rowling" },
            "J.K. Rowling",
            (_, _, _, _) => Task.CompletedTask);

        _ignoredPairRepository.Verify(
            r => r.DeleteInvolvingValuesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()),
            Times.Never);
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

    // ---- Ignored pairs ----

    [TestMethod]
    public async Task IgnorePairAsync_AddsOnePairPerAgainstValue_ExcludingTheValueItself()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "Ben Winters", "Ed Winters",
        });
        List<(string ValueA, string ValueB)>? insertedPairs = null;
        _ignoredPairRepository
            .Setup(r => r.AddRangeAsync("authors", It.IsAny<IEnumerable<(string ValueA, string ValueB)>>()))
            .Callback<string, IEnumerable<(string ValueA, string ValueB)>>((_, pairs) => insertedPairs = pairs.ToList())
            .Returns(Task.CompletedTask);

        await _service.IgnorePairAsync("authors", "Ben Winters", new List<string> { "Ed Winters", "Ben Winters" });

        Assert.IsNotNull(insertedPairs);
        Assert.AreEqual(1, insertedPairs!.Count, "the value itself must not be paired against itself");
        Assert.AreEqual(("Ben Winters", "Ed Winters"), insertedPairs[0], "the pair is ordered A < B ordinally");
    }

    [TestMethod]
    public async Task IgnorePairAsync_InvalidatesTheDetectionCache()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "Ben Winters", "Ed Winters",
        });
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());
        _ignoredPairRepository.Setup(r => r.AddRangeAsync(It.IsAny<string>(), It.IsAny<IEnumerable<(string ValueA, string ValueB)>>()))
            .Returns(Task.CompletedTask);

        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        await _service.IgnorePairAsync("authors", "Ben Winters", new List<string> { "Ed Winters" });

        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        // 3 calls: the first detect (cache miss), IgnorePairAsync's own existence check against
        // the current distinct values, and the second detect (cache invalidated by the ignore).
        _personRepository.Verify(r => r.GetAuthorNamesAsync(), Times.Exactly(3),
            "ignoring a pair must invalidate the cached grouping so it is re-clustered");
    }

    [TestMethod]
    public async Task IgnorePairAsync_NoAgainstValuesBesidesItself_DoesNotCallTheRepository()
    {
        await _service.IgnorePairAsync("authors", "Ben Winters", new List<string> { "Ben Winters" });

        _ignoredPairRepository.Verify(
            r => r.AddRangeAsync(It.IsAny<string>(), It.IsAny<IEnumerable<(string ValueA, string ValueB)>>()),
            Times.Never);
    }

    // Regression: a value that no longer exists in the library (e.g. a stale client tab still
    // holding a group an alignment has since folded away) must not be able to accumulate ignored
    // rows for it - IgnorePairAsync now checks both sides against the kind's current distinct
    // values before writing anything.
    [TestMethod]
    public async Task IgnorePairAsync_AgainstValueNoLongerExists_ReturnsFalseAndWritesNothing()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "Ben Winters",
        });

        var succeeded = await _service.IgnorePairAsync(
            "authors", "Ben Winters", new List<string> { "Ed Winters" });

        Assert.IsFalse(succeeded);
        _ignoredPairRepository.Verify(
            r => r.AddRangeAsync(It.IsAny<string>(), It.IsAny<IEnumerable<(string ValueA, string ValueB)>>()),
            Times.Never);
    }

    [TestMethod]
    public async Task DetectSimilarAuthorsAsync_IgnoredPair_SkipsThatEdge()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>
        {
            "Ben Winters", "Ed Winters",
        });
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());
        _ignoredPairRepository.Setup(r => r.GetForKindAsync("authors")).ReturnsAsync(new List<
            AudiobookManager.Database.Models.IgnoredSimilarValuePair>
        {
            new() { Id = 1, Kind = "authors", ValueA = "Ben Winters", ValueB = "Ed Winters", CreatedAt = DateTime.UtcNow },
        });

        var (groups, total) = await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        Assert.AreEqual(0, total, "an ignored pair must not be clustered together");
        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public async Task GetIgnoredPairsAsync_MapsRepositoryRowsToInfoRecords()
    {
        var ignoredAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _ignoredPairRepository.Setup(r => r.GetForKindAsync("series")).ReturnsAsync(new List<
            AudiobookManager.Database.Models.IgnoredSimilarValuePair>
        {
            new() { Id = 9, Kind = "series", ValueA = "A", ValueB = "B", CreatedAt = ignoredAt },
        });

        var pairs = await _service.GetIgnoredPairsAsync("series");

        Assert.AreEqual(1, pairs.Count);
        Assert.AreEqual(9, pairs[0].Id);
        Assert.AreEqual("A", pairs[0].ValueA);
        Assert.AreEqual("B", pairs[0].ValueB);
        Assert.AreEqual(ignoredAt, pairs[0].IgnoredAtUtc);
    }

    [TestMethod]
    public async Task RemoveIgnoredPairAsync_InvalidatesTheDetectionCache()
    {
        _personRepository.Setup(r => r.GetAuthorNamesAsync()).ReturnsAsync(new List<string>());
        _personRepository.Setup(r => r.GetAuthorBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());
        _ignoredPairRepository.Setup(r => r.DeleteAsync("authors", 5)).Returns(Task.CompletedTask);

        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        await _service.RemoveIgnoredPairAsync("authors", 5);

        await _service.DetectSimilarAuthorsAsync(skip: 0, take: 50);

        _personRepository.Verify(r => r.GetAuthorNamesAsync(), Times.Exactly(2),
            "removing an ignored pair must invalidate the cached grouping");
        _ignoredPairRepository.Verify(r => r.DeleteAsync("authors", 5), Times.Once);
    }

    // ---- Series "the"-insensitivity ----

    [TestMethod]
    public async Task DetectSimilarSeriesAsync_LeadingArticleDifference_IsGrouped()
    {
        _audiobookRepository.Setup(r => r.GetSeriesNamesAsync()).ReturnsAsync(new List<string>
        {
            "The Mistborn Saga", "Mistborn Saga",
        });
        _audiobookRepository.Setup(r => r.GetSeriesBookCountsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(new Dictionary<string, int>());

        var (groups, total) = await _service.DetectSimilarSeriesAsync(skip: 0, take: 50);

        Assert.AreEqual(1, total);
        CollectionAssert.AreEquivalent(
            new[] { "The Mistborn Saga", "Mistborn Saga" },
            groups[0].Candidates.Select(c => c.Value).ToList());
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_SeriesLeadingArticleDifference_IsSimilar()
    {
        _audiobookRepository.Setup(r => r.FindSeriesValueByFoldedNameAsync("Mistborn Saga"))
            .ReturnsAsync((string?)null);
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("Mistborn Saga", 20))
            .ReturnsAsync(new List<string> { "The Mistborn Saga" });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Series, "Mistborn Saga", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.AreEqual(1, status.SimilarMatches.Count);
        Assert.AreEqual("The Mistborn Saga", status.SimilarMatches[0].Name);
    }

    // Regression: the reverse direction of the leading-article rule. The prefilter's LIKE pattern
    // for "The Mistborn Saga" cannot find a stored "Mistborn Saga" by substring (the typed string
    // is the longer one), and its first-token fallback ("the") would flood the capped candidate
    // list with unrelated matches instead - so the service re-searches on the stripped form too.
    [TestMethod]
    public async Task GetEntryStatusAsync_SeriesLeadingArticleDifference_ReverseDirection_IsSimilar()
    {
        _audiobookRepository.Setup(r => r.FindSeriesValueByFoldedNameAsync("The Mistborn Saga"))
            .ReturnsAsync((string?)null);
        // The unstripped search only turns up noise the "the" token pattern floods in with - the
        // real match is missing until the stripped-form re-search runs.
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("The Mistborn Saga", 20))
            .ReturnsAsync(new List<string> { "The Wheel of Time" });
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("Mistborn Saga", 20))
            .ReturnsAsync(new List<string> { "Mistborn Saga" });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Series, "The Mistborn Saga", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.IsTrue(status.SimilarMatches.Any(m => m.Name == "Mistborn Saga"));
    }

    // Regression: each search is independently capped at the prefilter limit, so when the
    // unstripped "%the%" first-token fallback alone fills the cap (a library with 20+ series
    // containing "the" is not unusual), concatenating the stripped candidates *after* the
    // unstripped ones and then re-capping to the limit used to cut the genuine stripped match back
    // off before it was ever scored - reporting "New" for exactly the case this rule exists to fix.
    [TestMethod]
    public async Task GetEntryStatusAsync_SeriesLeadingArticleDifference_ReverseDirection_SurvivesUnstrippedFlood()
    {
        _audiobookRepository.Setup(r => r.FindSeriesValueByFoldedNameAsync("The Mistborn Saga"))
            .ReturnsAsync((string?)null);
        // The unstripped search's own capped result is entirely noise - 20 unrelated series whose
        // names happen to contain "the" - filling the candidate cap on its own.
        var flood = Enumerable.Range(1, 20).Select(i => $"The Noisy Series {i}").ToList();
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("The Mistborn Saga", 20))
            .ReturnsAsync(flood);
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("Mistborn Saga", 20))
            .ReturnsAsync(new List<string> { "Mistborn Saga" });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Series, "The Mistborn Saga", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.IsTrue(status.SimilarMatches.Any(m => m.Name == "Mistborn Saga"));
    }

    // ---- GetEntryStatusAsync ----

    [TestMethod]
    public async Task GetEntryStatusAsync_ExactExistingAuthor_IsExactWithTheMatchingRow()
    {
        _personRepository.Setup(r => r.FindAuthorByFoldedNameAsync("Brandon Sanderson"))
            .ReturnsAsync(new AuthorSummaryRow(7, "Brandon Sanderson", 5));

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Author, "Brandon Sanderson", 3);

        Assert.AreEqual(EntryValueStatusKind.Exact, status.Kind);
        Assert.IsNotNull(status.ExactMatch);
        Assert.AreEqual(7, status.ExactMatch.Id);
        Assert.AreEqual("Brandon Sanderson", status.ExactMatch.Name);
        Assert.AreEqual(0, status.SimilarMatches.Count);
        _personRepository.Verify(r => r.SearchAuthorNamesAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_AccentFoldVariantOfExistingAuthor_IsExact()
    {
        // "rene" and "René" fold to the same value, so the typed form is "exact existing" - the
        // accent-insensitive search invariant applies to this classification too.
        _personRepository.Setup(r => r.FindAuthorByFoldedNameAsync("rene"))
            .ReturnsAsync(new AuthorSummaryRow(3, "René", 2));

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Author, "rene", 3);

        Assert.AreEqual(EntryValueStatusKind.Exact, status.Kind);
        Assert.AreEqual("René", status.ExactMatch?.Name);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_SlightlyMisspelledExistingAuthor_IsSimilar()
    {
        _personRepository.Setup(r => r.FindAuthorByFoldedNameAsync("Brandon Sandersson"))
            .ReturnsAsync((AuthorSummaryRow?)null);
        _personRepository.Setup(r => r.SearchAuthorNamesAsync("Brandon Sandersson", 20))
            .ReturnsAsync(new List<AuthorSummaryRow>
            {
                new(7, "Brandon Sanderson", 5), // one-edit surname difference -> similar
            });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Author, "Brandon Sandersson", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.IsNull(status.ExactMatch);
        Assert.AreEqual(1, status.SimilarMatches.Count);
        Assert.AreEqual(7, status.SimilarMatches[0].Id);
        Assert.AreEqual("Brandon Sanderson", status.SimilarMatches[0].Name);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_UnknownAuthor_IsNew()
    {
        _personRepository.Setup(r => r.FindAuthorByFoldedNameAsync("Nova Writer"))
            .ReturnsAsync((AuthorSummaryRow?)null);
        _personRepository.Setup(r => r.SearchAuthorNamesAsync("Nova Writer", 20))
            .ReturnsAsync(new List<AuthorSummaryRow>());

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Author, "Nova Writer", 3);

        Assert.AreEqual(EntryValueStatusKind.New, status.Kind);
        Assert.IsNull(status.ExactMatch);
        Assert.AreEqual(0, status.SimilarMatches.Count);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_ExactExistingSeries_IsExact()
    {
        _audiobookRepository.Setup(r => r.FindSeriesValueByFoldedNameAsync("Mistborn"))
            .ReturnsAsync("Mistborn");

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Series, "Mistborn", 3);

        Assert.AreEqual(EntryValueStatusKind.Exact, status.Kind);
        Assert.IsNotNull(status.ExactMatch);
        Assert.IsNull(status.ExactMatch.Id, "series values carry no identity");
        Assert.AreEqual("Mistborn", status.ExactMatch.Name);
        _audiobookRepository.Verify(r => r.SearchSeriesValuesAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_NearDuplicateSeriesValue_IsSimilar()
    {
        _audiobookRepository.Setup(r => r.FindSeriesValueByFoldedNameAsync("The Stormlight Archivee"))
            .ReturnsAsync((string?)null);
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("The Stormlight Archivee", 20))
            .ReturnsAsync(new List<string> { "The Stormlight Archive" });
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("Stormlight Archivee", 20))
            .ReturnsAsync(new List<string>());

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Series, "The Stormlight Archivee", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.AreEqual(1, status.SimilarMatches.Count);
        Assert.AreEqual("The Stormlight Archive", status.SimilarMatches[0].Name);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_SameValueDifferentPunctuation_IsSimilar()
    {
        // Not an accent/case fold away ("J.K." vs "JK"), so not exact - but normalized-equal,
        // so similar. Mirrors the merge the alignment feature itself would do.
        _personRepository.Setup(r => r.FindAuthorByFoldedNameAsync("JK Rowling"))
            .ReturnsAsync((AuthorSummaryRow?)null);
        _personRepository.Setup(r => r.SearchAuthorNamesAsync("JK Rowling", 20))
            .ReturnsAsync(new List<AuthorSummaryRow> { new(9, "J.K. Rowling", 1) });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Author, "JK Rowling", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.AreEqual("J.K. Rowling", status.SimilarMatches[0].Name);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_SimilarMatches_AreCappedAtLimit()
    {
        _audiobookRepository.Setup(r => r.FindSeriesValueByFoldedNameAsync("Stormlight"))
            .ReturnsAsync((string?)null);
        _audiobookRepository.Setup(r => r.SearchSeriesValuesAsync("Stormlight", 20))
            .ReturnsAsync(new List<string>
            {
                "The Stormlight Archive",
                "The Stormlight Archivo",
                "The Stormlight Archives",
                "Stormlight Book 2",
            });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Series, "Stormlight", 2);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.AreEqual(2, status.SimilarMatches.Count);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_BlankValue_IsNewWithoutQueries()
    {
        var status = await _service.GetEntryStatusAsync(EntryValueKind.Author, "   ", 3);

        Assert.AreEqual(EntryValueStatusKind.New, status.Kind);
        _personRepository.Verify(r => r.FindAuthorByFoldedNameAsync(It.IsAny<string>()), Times.Never);
        _personRepository.Verify(r => r.SearchAuthorNamesAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _audiobookRepository.Verify(r => r.FindSeriesValueByFoldedNameAsync(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_ExactExistingNarrator_IsExact()
    {
        _personRepository.Setup(r => r.FindNarratorByFoldedNameAsync("Michael Kramer"))
            .ReturnsAsync(new AuthorSummaryRow(2, "Michael Kramer", 0));

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Narrator, "Michael Kramer", 3);

        Assert.AreEqual(EntryValueStatusKind.Exact, status.Kind);
        Assert.AreEqual(2, status.ExactMatch?.Id);
        Assert.AreEqual("Michael Kramer", status.ExactMatch?.Name);
        _personRepository.Verify(r => r.SearchNarratorNamesAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_SlightlyMisspelledExistingNarrator_IsSimilar()
    {
        _personRepository.Setup(r => r.FindNarratorByFoldedNameAsync("Michael Kramerr"))
            .ReturnsAsync((AuthorSummaryRow?)null);
        _personRepository.Setup(r => r.SearchNarratorNamesAsync("Michael Kramerr", 20))
            .ReturnsAsync(new List<AuthorSummaryRow> { new(2, "Michael Kramer", 0) });

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Narrator, "Michael Kramerr", 3);

        Assert.AreEqual(EntryValueStatusKind.Similar, status.Kind);
        Assert.AreEqual(1, status.SimilarMatches.Count);
        Assert.AreEqual("Michael Kramer", status.SimilarMatches[0].Name);
    }

    [TestMethod]
    public async Task GetEntryStatusAsync_NarratorClassification_NeverFallsThroughToSeries()
    {
        _personRepository.Setup(r => r.FindNarratorByFoldedNameAsync("Michael Kramer"))
            .ReturnsAsync((AuthorSummaryRow?)null);
        _personRepository.Setup(r => r.SearchNarratorNamesAsync("Michael Kramer", 20))
            .ReturnsAsync(new List<AuthorSummaryRow>());

        var status = await _service.GetEntryStatusAsync(EntryValueKind.Narrator, "Michael Kramer", 3);

        Assert.AreEqual(EntryValueStatusKind.New, status.Kind);
        _audiobookRepository.Verify(r => r.FindSeriesValueByFoldedNameAsync(It.IsAny<string>()), Times.Never);
        _audiobookRepository.Verify(r => r.SearchSeriesValuesAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
