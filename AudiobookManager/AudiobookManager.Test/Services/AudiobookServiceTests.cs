using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DbPerson = AudiobookManager.Database.Models.Person;
using DbGenre = AudiobookManager.Database.Models.Genre;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class AudiobookServiceTests
{
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IPersonRepository> _personRepository = null!;
    private Mock<IGenreRepository> _genreRepository = null!;
    private Mock<ILogger<AudiobookService>> _logger = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private AudiobookService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _personRepository = new Mock<IPersonRepository>();
        _genreRepository = new Mock<IGenreRepository>();
        _logger = new Mock<ILogger<AudiobookService>>();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = "/library"
        });

        _service = new AudiobookService(
            _tagHandler.Object,
            _settings,
            _audiobookRepository.Object,
            _personRepository.Object,
            _genreRepository.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task InsertAudiobook_CreatesPersonsAndGenres()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author1"), new Person("Author2") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/path/test.m4b", "test.m4b", 1000))
        {
            Narrators = new List<Person> { new Person("Narrator1") },
            Genres = new List<string> { "Fiction", "Sci-Fi" }
        };

        _personRepository.Setup(r => r.GetOrCreatePersons(It.Is<IEnumerable<string>>(names =>
                names.SequenceEqual(new[] { "Author1", "Author2", "Narrator1" }))))
            .ReturnsAsync(new Dictionary<string, DbPerson>
            {
                ["Author1"] = new DbPerson(1, "Author1"),
                ["Author2"] = new DbPerson(2, "Author2"),
                ["Narrator1"] = new DbPerson(3, "Narrator1")
            });

        _genreRepository.Setup(r => r.GetOrCreateGenres(It.Is<IEnumerable<string>>(names =>
                names.SequenceEqual(new[] { "Fiction", "Sci-Fi" }))))
            .ReturnsAsync(new Dictionary<string, DbGenre>
            {
                ["Fiction"] = new DbGenre(1, "Fiction"),
                ["Sci-Fi"] = new DbGenre(2, "Sci-Fi")
            });

        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 1;
                return db;
            });

        var result = await _service.InsertAudiobook(audiobook);

        _personRepository.Verify(r => r.GetOrCreatePersons(It.IsAny<IEnumerable<string>>()), Times.Once);
        _genreRepository.Verify(r => r.GetOrCreateGenres(It.IsAny<IEnumerable<string>>()), Times.Once);
        _audiobookRepository.Verify(r => r.InsertAudiobook(It.Is<DbAudiobook>(db =>
            db.BookName == "Test Book" &&
            db.Authors.Count == 2 &&
            db.Narrators.Count == 1 &&
            db.Genres.Count == 2
        )), Times.Once);

        Assert.AreEqual("Test Book", result.BookName);
    }

    [TestMethod]
    public async Task InsertAudiobook_EmptyAuthorsAndNarrators_Succeeds()
    {
        var audiobook = new Audiobook(
            new List<Person>(),
            "Solo Book",
            2024,
            new AudiobookFileInfo("/path/solo.m4b", "solo.m4b", 500));

        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 1;
                return db;
            });

        var result = await _service.InsertAudiobook(audiobook);

        _personRepository.Verify(r => r.GetOrCreatePersons(It.Is<IEnumerable<string>>(names => !names.Any())), Times.Once);
        _genreRepository.Verify(r => r.GetOrCreateGenres(It.Is<IEnumerable<string>>(names => !names.Any())), Times.Once);
        _audiobookRepository.Verify(r => r.InsertAudiobook(It.Is<DbAudiobook>(db =>
            db.Authors.Count == 0 &&
            db.Narrators.Count == 0 &&
            db.Genres.Count == 0
        )), Times.Once);

        Assert.AreEqual("Solo Book", result.BookName);
    }

    [TestMethod]
    public void GenerateLibraryPath_ReturnsExpectedPath()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("John Smith") },
            "My Book",
            2024,
            new AudiobookFileInfo("/import/test.m4b", "test.m4b", 1000));

        var result = _service.GenerateLibraryPath(audiobook);

        Assert.IsTrue(result.StartsWith("/library"));
        Assert.IsTrue(result.Contains("John Smith"));
        Assert.IsTrue(result.Contains("My Book"));
    }

    [TestMethod]
    public void ParseAudiobook_CallsTagHandler()
    {
        var expected = new Audiobook(
            new List<Person> { new Person("Author") },
            "Parsed Book",
            2024,
            new AudiobookFileInfo("/path/book.m4b", "book.m4b", 1000));

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
            .Returns(expected);

        var result = _service.ParseAudiobook("/path/book.m4b");

        Assert.AreEqual("Parsed Book", result.BookName);
        _tagHandler.Verify(t => t.ParseAudiobook(It.Is<FileInfo>(fi => fi.FullName == "/path/book.m4b")), Times.Once);
    }

    #region UpdateAudiobook

    private string _testRoot = null!;
    private string _libraryPath = null!;

    private void SetupUpdateAudiobookTest()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "audiobook-service-tests-" + Guid.NewGuid());
        _libraryPath = Path.Combine(_testRoot, "library");
        Directory.CreateDirectory(_libraryPath);

        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = _libraryPath
        });

        _service = new AudiobookService(
            _tagHandler.Object,
            _settings,
            _audiobookRepository.Object,
            _personRepository.Object,
            _genreRepository.Object,
            _logger.Object);
    }

    private static void CleanupTestRoot(string? testRoot)
    {
        if (testRoot is not null && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static DbAudiobook CreateExistingDbAudiobook(long id, string filePath, string? series = null, string? seriesPart = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "original m4b content");

        return new DbAudiobook(
            id, "Old Book Name", null, series, seriesPart, 2020,
            "Old description", null, null, null, null, null, null, null,
            filePath, Path.GetFileName(filePath), 1000)
        {
            Authors = new List<DbPerson> { new DbPerson(1, "Old Author") }
        };
    }

    private void SetupCommonRepositoryMocks(long id, DbAudiobook existing)
    {
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(id)).ReturnsAsync(existing);
        _personRepository.Setup(r => r.GetOrCreatePerson(It.IsAny<string>()))
            .ReturnsAsync((string name) => new DbPerson(1, name));
        _personRepository.Setup(r => r.GetOrCreatePersons(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> names) => names.Distinct().ToDictionary(n => n, n => new DbPerson(1, n)));
        _audiobookRepository.Setup(r => r.UpdateAudiobookAsync(It.IsAny<DbAudiobook>())).Returns(Task.CompletedTask);
    }

    [TestCleanup]
    public void CleanupUpdateAudiobookTest()
    {
        CleanupTestRoot(_testRoot);
    }

    [TestMethod]
    public async Task UpdateAudiobook_PathUnchanged_DoesNotMoveFileButRewritesTagsAndSidecars()
    {
        SetupUpdateAudiobookTest();

        // Compute the path first so the "existing" file already lives exactly where the
        // updated metadata will generate a path for - i.e. only non-path fields change.
        var author = new Person("Same Author");
        var probe = new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));
        var expectedPath = _service.GenerateLibraryPath(probe);

        var existing = CreateExistingDbAudiobook(1, expectedPath);
        existing.BookName = "Same Book";
        existing.Authors = new List<DbPerson> { new DbPerson(1, "Same Author") };
        SetupCommonRepositoryMocks(1, existing);

        Audiobook? tagsSavedFor = null;
        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()))
            .Callback<Audiobook, Action<float>?>((a, _) => tagsSavedFor = a);

        var reparsed = new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo(expectedPath, Path.GetFileName(expectedPath), 1000))
        {
            Description = "Updated description",
            Narrators = new List<Person> { new Person("Narrator One") }
        };
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>())).Returns(reparsed);

        var updateDto = new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Description = "Updated description",
            Narrators = new List<Person> { new Person("Narrator One") }
        };

        var result = await _service.UpdateAudiobook(1, updateDto);

        Assert.IsTrue(File.Exists(expectedPath), "File should still exist at its original/unchanged path");
        Assert.AreEqual(expectedPath, result.FileInfo.FullPath);

        _tagHandler.Verify(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()), Times.Once);
        Assert.IsNotNull(tagsSavedFor);

        var directory = Path.GetDirectoryName(expectedPath)!;
        Assert.IsTrue(File.Exists(Path.Combine(directory, "desc.txt")), "desc.txt sidecar should be (re)written even without relocation");
        Assert.AreEqual("Updated description", File.ReadAllText(Path.Combine(directory, "desc.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "reader.txt")), "reader.txt sidecar should be (re)written even without relocation");

        _audiobookRepository.Verify(r => r.UpdateAudiobookAsync(It.IsAny<DbAudiobook>()), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_PathChanged_RelocatesFileAndCleansUpStaleSidecars()
    {
        SetupUpdateAudiobookTest();

        var oldFilePath = Path.Combine(_libraryPath, "Old Author", "2020 - Old Book Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath);

        var oldDirectory = Path.GetDirectoryName(oldFilePath)!;
        var staleDesc = Path.Combine(oldDirectory, "desc.txt");
        var staleReader = Path.Combine(oldDirectory, "reader.txt");
        var staleCover = Path.Combine(oldDirectory, "cover.jpg");
        File.WriteAllText(staleDesc, "stale description");
        File.WriteAllText(staleReader, "stale narrator");
        File.WriteAllText(staleCover, "stale cover bytes");

        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var newAuthor = new Person("New Author");
        var updateDto = new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Description = "New description",
            Narrators = new List<Person> { new Person("New Narrator") }
        };

        var expectedNewPath = _service.GenerateLibraryPath(new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0)));

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
            .Returns((FileInfo fi) => new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Description = "New description",
                Narrators = new List<Person> { new Person("New Narrator") }
            });

        var result = await _service.UpdateAudiobook(1, updateDto);

        Assert.AreEqual(expectedNewPath, result.FileInfo.FullPath);
        Assert.IsTrue(File.Exists(expectedNewPath), "File should have been relocated to the new path");
        Assert.IsFalse(File.Exists(oldFilePath), "File should no longer exist at the old path");

        Assert.IsFalse(File.Exists(staleDesc), "Stale desc.txt at the old directory should be cleaned up");
        Assert.IsFalse(File.Exists(staleReader), "Stale reader.txt at the old directory should be cleaned up");
        Assert.IsFalse(File.Exists(staleCover), "Stale cover.jpg at the old directory should be cleaned up");
        Assert.IsFalse(Directory.Exists(oldDirectory), "Now-empty old directory should be removed");

        var newDirectory = Path.GetDirectoryName(expectedNewPath)!;
        Assert.IsTrue(File.Exists(Path.Combine(newDirectory, "desc.txt")), "desc.txt should be written at the new location");
        Assert.AreEqual("New description", File.ReadAllText(Path.Combine(newDirectory, "desc.txt")));

        _audiobookRepository.Verify(r => r.UpdateAudiobookAsync(It.Is<DbAudiobook>(db => db.FileInfoFullPath == expectedNewPath)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_ClearingSeries_RegeneratesPathWithoutSeriesSegment()
    {
        SetupUpdateAudiobookTest();

        var oldFilePath = Path.Combine(_libraryPath, "Old Author", "Old Series", "Book 01 - 2020 - Old Book Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath, series: "Old Series", seriesPart: "1");
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var author = new Person("Old Author");
        var updateDto = new Audiobook(new List<Person> { author }, "Old Book Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Series = null,
            SeriesPart = null
        };

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
            .Returns((FileInfo fi) => new Audiobook(new List<Person> { author }, "Old Book Name", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000)));

        var result = await _service.UpdateAudiobook(1, updateDto);

        Assert.IsFalse(result.FileInfo.FullPath.Contains("Old Series"), "Regenerated path should not include the cleared series segment");
        Assert.IsTrue(File.Exists(result.FileInfo.FullPath));

        _audiobookRepository.Verify(r => r.UpdateAudiobookAsync(It.Is<DbAudiobook>(db => db.Series == null && db.SeriesPart == null)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateAudiobook_TagWriteThrows_ExceptionPropagatesAndDbIsNotUpdated()
    {
        SetupUpdateAudiobookTest();

        var oldFilePath = Path.Combine(_libraryPath, "Old Author", "2020 - Old Book Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath);
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()))
            .Throws(new InvalidOperationException("tag write failed"));

        var updateDto = new Audiobook(new List<Person> { new Person("Old Author") }, "Old Book Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.UpdateAudiobook(1, updateDto));

        Assert.IsTrue(File.Exists(oldFilePath), "File must not have been relocated since tag write failed before relocation");
        _audiobookRepository.Verify(r => r.UpdateAudiobookAsync(It.IsAny<DbAudiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAudiobook_TagsDoNotRoundTripAfterSave_ThrowsAndDoesNotUpdateDbOrRelocate()
    {
        // Regression test: a save must not silently succeed when the tags actually written to
        // the m4b (as re-parsed from disk) don't match what was requested - that desync is
        // exactly what immediately re-surfaces as "Wrong File Path"/"Tag Mismatch" consistency
        // issues right after a save, even though the save reported success.
        SetupUpdateAudiobookTest();

        var oldFilePath = Path.Combine(_libraryPath, "Old Author", "2020 - Old Book Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath);
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var newAuthor = new Person("New Author");
        var updateDto = new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Narrators = new List<Person> { new Person("New Narrator") }
        };

        // Simulate the tag write not actually persisting the narrator (leaving a stale value on
        // disk) even though SaveAudiobookTagsToFile reported success.
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>()))
            .Returns((FileInfo fi) => new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Narrators = new List<Person> { new Person("Stale Old Narrator") }
            });

        await Assert.ThrowsExactlyAsync<Exception>(() => _service.UpdateAudiobook(1, updateDto));

        Assert.IsTrue(File.Exists(oldFilePath), "File must not have been relocated since the round-trip check failed before relocation");
        _audiobookRepository.Verify(r => r.UpdateAudiobookAsync(It.IsAny<DbAudiobook>()), Times.Never);
    }

    #endregion

    #region CheckTargetPathCollision

    private static Audiobook MakeAudiobookForCollisionCheck(string bookName = "Children of Time", int year = 2016, string sourcePath = "/import/book.m4b") =>
        new Audiobook(
            new List<Person> { new Person("Adrian Tchaikovsky") },
            bookName,
            year,
            new AudiobookFileInfo(sourcePath, Path.GetFileName(sourcePath), 12345));

    [TestMethod]
    public async Task CheckTargetPathCollision_NoFileAtTarget_ReturnsNotExists()
    {
        // Reuses the real-temp-directory setup from the UpdateAudiobook region since this
        // also needs File.Exists to observe a real filesystem rather than the "/library" stub.
        SetupUpdateAudiobookTest();
        var book = MakeAudiobookForCollisionCheck();

        var result = await _service.CheckTargetPathCollision(book);

        Assert.IsFalse(result.Exists);
        Assert.IsNull(result.ExistingAudiobookId);
        Assert.IsNull(result.ExistingSizeInBytes);
        Assert.IsNull(result.ExistingDurationInSeconds);
        Assert.AreEqual(_service.GenerateLibraryPath(book), result.TargetPath);
    }

    [TestMethod]
    public async Task CheckTargetPathCollision_TargetOccupiedByTrackedAudiobook_ReturnsExistingBookDetails()
    {
        SetupUpdateAudiobookTest();
        var book = MakeAudiobookForCollisionCheck();
        var targetPath = _service.GenerateLibraryPath(book);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllTextAsync(targetPath, "existing audio");

        var existingDbBook = new DbAudiobook(
            id: 42, bookName: "Children of Time", subtitle: null, series: null, seriesPart: null,
            year: 2016, description: null, copyright: null, publisher: null, rating: null,
            asin: null, www: null, coverFilePath: null, durationInSeconds: 39600,
            fileInfoFullPath: targetPath, fileInfoFileName: Path.GetFileName(targetPath), fileInfoSizeInBytes: 598_000_000);

        _audiobookRepository.Setup(r => r.GetByFullPathAsync(targetPath)).ReturnsAsync(existingDbBook);

        var result = await _service.CheckTargetPathCollision(book);

        Assert.IsTrue(result.Exists);
        Assert.AreEqual(42, result.ExistingAudiobookId);
        Assert.AreEqual(598_000_000, result.ExistingSizeInBytes);
        Assert.AreEqual(39600, result.ExistingDurationInSeconds);
    }

    [TestMethod]
    public async Task CheckTargetPathCollision_TargetOccupiedByUntrackedFile_ReturnsFileSizeWithoutAudiobookIdOrDuration()
    {
        SetupUpdateAudiobookTest();
        var book = MakeAudiobookForCollisionCheck();
        var targetPath = _service.GenerateLibraryPath(book);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var content = "orphaned file contents";
        await File.WriteAllTextAsync(targetPath, content);

        _audiobookRepository.Setup(r => r.GetByFullPathAsync(targetPath)).ReturnsAsync((DbAudiobook?)null);

        var result = await _service.CheckTargetPathCollision(book);

        Assert.IsTrue(result.Exists);
        Assert.IsNull(result.ExistingAudiobookId);
        Assert.AreEqual(content.Length, result.ExistingSizeInBytes);
        Assert.IsNull(result.ExistingDurationInSeconds);
    }

    #endregion
}
