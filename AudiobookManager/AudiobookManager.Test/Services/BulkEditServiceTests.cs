using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;
using DbGenre = AudiobookManager.Database.Models.Genre;
using DbPerson = AudiobookManager.Database.Models.Person;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class BulkEditServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IAudiobookService> _audiobookService = null!;
    private Mock<ILibraryConsistencyService> _libraryConsistencyService = null!;
    private Mock<ISimilarValueDetectionCache> _detectionCache = null!;
    private Mock<ILogger<BulkEditService>> _logger = null!;
    private AudiobookSaveGate _saveGate = null!;
    private BulkEditService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _audiobookService = new Mock<IAudiobookService>();
        _libraryConsistencyService = new Mock<ILibraryConsistencyService>();
        _detectionCache = new Mock<ISimilarValueDetectionCache>();
        _logger = new Mock<ILogger<BulkEditService>>();
        _saveGate = new AudiobookSaveGate();

        _service = new BulkEditService(
            _audiobookRepository.Object,
            _audiobookService.Object,
            _libraryConsistencyService.Object,
            _saveGate,
            _detectionCache.Object,
            _logger.Object);
    }

    private static DbAudiobook MakeDbAudiobook(long id, string bookName)
    {
        var book = new DbAudiobook(
            id, bookName, null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000);
        book.Authors = new List<DbPerson> { new DbPerson(default, "Test Author") };
        book.Narrators = new List<DbPerson>();
        book.Genres = new List<DbGenre>();
        return book;
    }

    private static Audiobook MakeBook() => new Audiobook(
        new List<Person> { new Person("Test Author") },
        "A Book",
        2024,
        new AudiobookFileInfo("/library/a.m4b", "a.m4b", 1000))
    {
        Subtitle = "Orig subtitle",
        Series = "A Series",
        SeriesPart = "2",
        Genres = new List<string> { "Fantasy" },
        Description = "Orig description",
        Narrators = new List<Person> { new Person("Orig Narrator") }
    };

    private static Task NoOpProgress(int processed, int total, int succeeded, int failed) => Task.CompletedTask;

    #region ApplyTo semantics

    // An absent change must leave the field untouched - there is no implicit clearing.
    [TestMethod]
    public void ApplyTo_ASetChange_ChangesOnlyTheTouchedField()
    {
        var book = MakeBook();

        new AudiobookBulkChanges { BookName = new BulkSingleChange("New Title", Clear: false) }.ApplyTo(book);

        Assert.AreEqual("New Title", book.BookName);
        Assert.AreEqual("Orig subtitle", book.Subtitle);
        Assert.AreEqual("A Series", book.Series);
        Assert.AreEqual("2", book.SeriesPart);
        Assert.AreEqual("Orig description", book.Description);
        Assert.AreEqual(2024, book.Year);
        Assert.AreEqual("Test Author", book.Authors.Single().Name);
        Assert.AreEqual("Orig Narrator", book.Narrators.Single().Name);
        CollectionAssert.AreEqual(new List<string> { "Fantasy" }, book.Genres);
    }

    [TestMethod]
    public void ApplyTo_AClearChange_NullsTheField()
    {
        var book = MakeBook();

        new AudiobookBulkChanges { Subtitle = new BulkSingleChange(null, Clear: true) }.ApplyTo(book);

        Assert.IsNull(book.Subtitle);
        Assert.AreEqual("A Book", book.BookName, "an absent change on BookName leaves it untouched");
    }

    [TestMethod]
    public void ApplyTo_AYearClear_NullsTheYear()
    {
        var book = MakeBook();

        new AudiobookBulkChanges { Year = new BulkIntChange(null, Clear: true) }.ApplyTo(book);

        Assert.IsNull(book.Year);
        Assert.AreEqual("A Book", book.BookName, "an absent change on BookName leaves it untouched");
    }

    [TestMethod]
    public void ApplyTo_ASetYear_AssignsTheValue()
    {
        var book = MakeBook();

        new AudiobookBulkChanges { Year = new BulkIntChange(1999, Clear: false) }.ApplyTo(book);

        Assert.AreEqual(1999, book.Year);
    }

    [TestMethod]
    public void ApplyTo_AMultiReplace_AssignsTheList()
    {
        var book = MakeBook();

        new AudiobookBulkChanges
        {
            Genres = new BulkMultiChange("replace", new List<string> { "Sci-Fi", "Fantasy" }),
            Authors = new BulkMultiChange("replace", new List<string> { "New Author" })
        }.ApplyTo(book);

        CollectionAssert.AreEqual(new List<string> { "Sci-Fi", "Fantasy" }, book.Genres);
        Assert.AreEqual("New Author", book.Authors.Single().Name);
    }

    // "add" appends only values not already present, comparing case-insensitively, preserving
    // the existing order and then the new values in the order given.
    [TestMethod]
    public void ApplyTo_AMultiAdd_AppendsOnlyValuesNotAlreadyPresentCaseInsensitively()
    {
        var book = MakeBook();
        book.Authors = new List<Person> { new Person("J.K. Rowling"), new Person("Jim") };

        new AudiobookBulkChanges
        {
            Authors = new BulkMultiChange("add", new List<string> { "j.k. rowling", "Neil Gaiman", "NEIL gaiman", "jim" })
        }.ApplyTo(book);

        Assert.AreEqual(3, book.Authors.Count);
        Assert.AreEqual("J.K. Rowling", book.Authors[0].Name, "existing values keep their order and spelling");
        Assert.AreEqual("Jim", book.Authors[1].Name);
        Assert.AreEqual("Neil Gaiman", book.Authors[2].Name, "the first spelling of a new value wins");
    }

    // A clear change is the explicit way to empty a multi-value field: it assigns an empty list,
    // leaving every other field untouched.
    [TestMethod]
    public void ApplyTo_AClearNarratorsAndGenres_EmptiesOnlyThoseLists()
    {
        var book = MakeBook();

        new AudiobookBulkChanges
        {
            Narrators = new BulkMultiChange(null, new List<string>(), Clear: true),
            Genres = new BulkMultiChange(null, new List<string>(), Clear: true)
        }.ApplyTo(book);

        CollectionAssert.AreEqual(new List<Person>(), book.Narrators);
        CollectionAssert.AreEqual(new List<string>(), book.Genres);
        Assert.AreEqual("Test Author", book.Authors.Single().Name, "an absent change on Authors leaves it untouched");
        Assert.AreEqual("A Book", book.BookName, "an absent change on BookName leaves it untouched");
        Assert.AreEqual(2024, book.Year, "an absent change on Year leaves it untouched");
    }

    // The set value is trimmed before it is stored.
    [TestMethod]
    public void ApplyTo_ASetChange_TrimsTheValue()
    {
        var book = MakeBook();

        new AudiobookBulkChanges { Publisher = new BulkSingleChange("  Puffin Books  ", Clear: false) }.ApplyTo(book);

        Assert.AreEqual("Puffin Books", book.Publisher);
    }

    #endregion

    #region ApplyAsync

    [TestMethod]
    public async Task ApplyAsync_PassesTheIdAndTheChangedDomainToUpdateAudiobook_ThenRechecks()
    {
        var dbBook = MakeDbAudiobook(1, "A Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 1 }))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        Audiobook? captured = null;
        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()))
            .Callback<long, Audiobook, Func<string, int, Task>?>((id, a, _) => captured = a)
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? progressAction) => a);

        var result = await _service.ApplyAsync(
            new AudiobookBulkChanges { BookName = new BulkSingleChange("Renamed", Clear: false) },
            new List<long> { 1 },
            NoOpProgress);

        Assert.AreEqual((1, 1, 0), result);
        Assert.IsNotNull(captured);
        Assert.AreEqual("Renamed", captured!.BookName);
        Assert.AreEqual(2024, captured.Year, "fields without a change keep their values");
        Assert.AreEqual("Test Author", captured.Authors.Single().Name);
        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()), Times.Once);
        _libraryConsistencyService.Verify(s => s.RecheckAudiobookAsync(1), Times.Once);
    }

    // A book being saved right now fails just its own item; the batch carries on - the same
    // shape as the alignment loops.
    [TestMethod]
    public async Task ApplyAsync_ABookWhoseGateIsHeld_FailsJustThatItem()
    {
        var busy = MakeDbAudiobook(4001, "Busy Book");
        var free = MakeDbAudiobook(4002, "Free Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<DbAudiobook> { busy, free });
        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(It.IsAny<long>()))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        using var lease = _saveGate.Acquire(4001);

        var progressCalls = new List<(int processed, int total, int succeeded, int failed)>();
        var result = await _service.ApplyAsync(
            new AudiobookBulkChanges { BookName = new BulkSingleChange("Renamed", Clear: false) },
            new List<long> { 4001, 4002 },
            (processed, total, succeeded, failed) =>
            {
                progressCalls.Add((processed, total, succeeded, failed));
                return Task.CompletedTask;
            });

        Assert.AreEqual((2, 1, 1), result);
        _audiobookService.Verify(s => s.UpdateAudiobook(4001, It.IsAny<Audiobook>()), Times.Never);
        _audiobookService.Verify(s => s.UpdateAudiobook(4002, It.IsAny<Audiobook>()), Times.Once);
        Assert.AreEqual((2, 2, 1, 1), progressCalls.Last());
    }

    // Mirrors the save endpoint: a recheck failure is logged and swallowed, never an item failure.
    [TestMethod]
    public async Task ApplyAsync_ARecheckFailure_DoesNotFailTheItem()
    {
        var dbBook = MakeDbAudiobook(1, "A Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 1 }))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });
        _audiobookService.Setup(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(1))
            .ThrowsAsync(new Exception("recheck boom"));

        var result = await _service.ApplyAsync(
            new AudiobookBulkChanges { BookName = new BulkSingleChange("Renamed", Clear: false) },
            new List<long> { 1 },
            NoOpProgress);

        Assert.AreEqual((1, 1, 0), result);
        _audiobookService.Verify(s => s.UpdateAudiobook(1, It.IsAny<Audiobook>()), Times.Once);
    }

    // The controller refuses a change set that would leave a book without an author; the guard
    // must still hold if a change set reaches the service directly (e.g. through a future UI).
    [TestMethod]
    public async Task ApplyAsync_ACollectiveThatWouldLeaveNoAuthor_FailsJustThatItem()
    {
        var dbBook = MakeDbAudiobook(1, "A Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 1 }))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });

        var result = await _service.ApplyAsync(
            new AudiobookBulkChanges { Authors = new BulkMultiChange("replace", new List<string>()) },
            new List<long> { 1 },
            NoOpProgress);

        Assert.AreEqual((1, 0, 1), result);
        _audiobookService.Verify(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task GetPreviewAsync_MapsTheLoadedBooksWithTheirCurrentValues()
    {
        var dbBook = MakeDbAudiobook(7, "A Book");
        dbBook.Subtitle = "A subtitle";
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(new List<long> { 7 }))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });

        var preview = await _service.GetPreviewAsync(new List<long> { 7 });

        Assert.AreEqual(1, preview.Count);
        Assert.AreEqual(7, preview[0].Id);
        Assert.AreEqual("A Book", preview[0].BookName);
        Assert.AreEqual("A subtitle", preview[0].Subtitle);
        Assert.AreEqual(2024, preview[0].Year);
        Assert.AreEqual("Test Author", preview[0].Authors.Single().Name);
    }

    #endregion

    #region Detection cache invalidation

    [TestMethod]
    public async Task ApplyAsync_AnAuthorsChange_InvalidatesTheDetectionCache()
    {
        var dbBook = MakeDbAudiobook(1, "A Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });
        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(It.IsAny<long>()))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        await _service.ApplyAsync(
            new AudiobookBulkChanges { Authors = new BulkMultiChange("replace", new List<string> { "New Author" }) },
            new List<long> { 1 },
            NoOpProgress);

        _detectionCache.Verify(c => c.Invalidate(), Times.Once);
    }

    [TestMethod]
    public async Task ApplyAsync_ASeriesChange_InvalidatesTheDetectionCache()
    {
        var dbBook = MakeDbAudiobook(1, "A Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });
        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(It.IsAny<long>()))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        await _service.ApplyAsync(
            new AudiobookBulkChanges { Series = new BulkSingleChange("New Series", Clear: false) },
            new List<long> { 1 },
            NoOpProgress);

        _detectionCache.Verify(c => c.Invalidate(), Times.Once);
    }

    [TestMethod]
    public async Task ApplyAsync_AChangeNotTouchingAuthorsOrSeries_DoesNotInvalidateTheDetectionCache()
    {
        var dbBook = MakeDbAudiobook(1, "A Book");
        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<DbAudiobook> { dbBook });
        _audiobookService.Setup(s => s.UpdateAudiobook(It.IsAny<long>(), It.IsAny<Audiobook>()))
            .ReturnsAsync((long id, Audiobook a, Func<string, int, Task>? _) => a);
        _libraryConsistencyService.Setup(s => s.RecheckAudiobookAsync(It.IsAny<long>()))
            .ReturnsAsync(new List<Database.Models.ConsistencyIssue>());

        await _service.ApplyAsync(
            new AudiobookBulkChanges { BookName = new BulkSingleChange("Renamed", Clear: false) },
            new List<long> { 1 },
            NoOpProgress);

        _detectionCache.Verify(c => c.Invalidate(), Times.Never);
    }

    #endregion
}