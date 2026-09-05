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
    private IAudiobookFileHandler _fileHandler = null!;
    private IFileOperations _fileOperations = null!;
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IPersonRepository> _personRepository = null!;
    private Mock<IGenreRepository> _genreRepository = null!;
    private Mock<IConsistencyIssueRepository> _issueRepository = null!;
    private Mock<ILogger<AudiobookService>> _logger = null!;
    private IOptions<AudiobookManagerSettings> _settings = null!;
    private AudiobookService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _fileOperations = new FileOperations();
        _fileHandler = new AudiobookFileHandler(_fileOperations);
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _personRepository = new Mock<IPersonRepository>();
        _genreRepository = new Mock<IGenreRepository>();
        _issueRepository = new Mock<IConsistencyIssueRepository>();
        _logger = new Mock<ILogger<AudiobookService>>();
        _settings = Options.Create(new AudiobookManagerSettings
        {
            AudiobookLibraryPath = "/library"
        });

        _service = new AudiobookService(
            _tagHandler.Object,
            _fileHandler,
            _fileOperations,
            _settings,
            _audiobookRepository.Object,
            _personRepository.Object,
            _genreRepository.Object,
            _issueRepository.Object,
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
    public async Task InsertAudiobook_MapsLanguageThroughToFromDb()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author1") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/path/test.m4b", "test.m4b", 1000))
        {
            Language = "English"
        };

        _personRepository.Setup(r => r.GetOrCreatePersons(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, DbPerson> { ["Author1"] = new DbPerson(1, "Author1") });
        _genreRepository.Setup(r => r.GetOrCreateGenres(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, DbGenre>());

        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 1;
                return db;
            });

        var result = await _service.InsertAudiobook(audiobook);

        _audiobookRepository.Verify(r => r.InsertAudiobook(It.Is<DbAudiobook>(db => db.Language == "English")), Times.Once);
        Assert.AreEqual("English", result.Language);
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
    public void GenerateLibraryPath_TraversalInTags_StaysInsideLibraryRoot()
    {
        // Tag values are user- and scrape-controlled, and nothing between here and the File.Move
        // that acts on this path re-checks the destination.
        var audiobook = new Audiobook(
            new List<Person> { new Person("..") },
            "..",
            2024,
            new AudiobookFileInfo("/import/test.m4b", "test.m4b", 1000))
        {
            Series = ".."
        };

        var result = _service.GenerateLibraryPath(audiobook);

        Assert.IsTrue(
            AudiobookFileHandler.PathStartsWith(result, "/library"),
            $"Generated path escaped the library root: {result}");
    }

    [TestMethod]
    public void ParseAudiobook_CallsTagHandler()
    {
        var expected = new Audiobook(
            new List<Person> { new Person("Author") },
            "Parsed Book",
            2024,
            new AudiobookFileInfo("/path/book.m4b", "book.m4b", 1000));

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns(expected);

        var result = _service.ParseAudiobook("/path/book.m4b");

        Assert.AreEqual("Parsed Book", result.BookName);
        _tagHandler.Verify(t => t.ParseAudiobook(It.Is<FileInfo>(fi => fi.FullName == "/path/book.m4b"), It.IsAny<bool>()), Times.Once);
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
            _fileHandler,
            _fileOperations,
            _settings,
            _audiobookRepository.Object,
            _personRepository.Object,
            _genreRepository.Object,
            _issueRepository.Object,
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
            "Old description", null, null, null, null, null, null, null, null,
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
            Narrators = new List<Person> { new Person("Narrator One") },
            Language = "English"
        };
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(reparsed);

        var updateDto = new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Description = "Updated description",
            Narrators = new List<Person> { new Person("Narrator One") },
            Language = "English"
        };

        var result = await _service.UpdateAudiobook(1, updateDto);

        Assert.AreEqual("English", existing.Language);

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

    // Regression: Genres is a many-to-many relation with no position column, so nothing
    // preserved caller-supplied order anywhere in the pipeline - resaving the identical genre set
    // in a different order (e.g. the client re-splits a free-text field) produced a different "/"
    // -joined tag string every time, which looked like a real metadata change on every save. The
    // shared WriteTagsRelocateAndWriteSidecarsAsync pipeline now sorts genres alphabetically
    // before writing tags, so the same genre set always produces the same tag regardless of the
    // order it was submitted in.
    [TestMethod]
    public async Task UpdateAudiobook_GenresSubmittedOutOfOrder_AreSortedBeforeTagsAreWritten()
    {
        SetupUpdateAudiobookTest();

        var author = new Person("Same Author");
        var probe = new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));
        var expectedPath = _service.GenerateLibraryPath(probe);

        var existing = CreateExistingDbAudiobook(1, expectedPath);
        existing.BookName = "Same Book";
        existing.Authors = new List<DbPerson> { new DbPerson(1, "Same Author") };
        SetupCommonRepositoryMocks(1, existing);
        _genreRepository.Setup(r => r.GetOrCreateGenres(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> names) => names.Distinct().ToDictionary(n => n, n => new DbGenre(1, n)));

        Audiobook? tagsSavedFor = null;
        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()))
            .Callback<Audiobook, Action<float>?>((a, _) => tagsSavedFor = a);

        // ParseAudiobook simulates the real tag round-trip: whatever order was actually written
        // (tagsSavedFor.Genres) is what re-reading the file gives back.
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Genres = tagsSavedFor!.Genres.ToList()
            });

        var updateDto = new Audiobook(new List<Person> { author }, "Same Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Genres = new List<string> { "Zeta Genre", "Alpha Genre", "Mid Genre" }
        };

        var result = await _service.UpdateAudiobook(1, updateDto);

        var expectedOrder = new List<string> { "Alpha Genre", "Mid Genre", "Zeta Genre" };
        CollectionAssert.AreEqual(expectedOrder, tagsSavedFor!.Genres);
        CollectionAssert.AreEqual(expectedOrder, result.Genres);
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

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
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
    public async Task UpdateAudiobook_Relocation_LeavesSidecarsInTheDestinationDirectoryIntact()
    {
        // Cleanup must only ever touch the directory the book left, never the one it arrived in.
        // reader.txt is the sharp edge: RemoveSidecarFiles deletes it outright, so a cleanup
        // pointed at the destination would remove the one WriteMetadata had just written there.
        //
        // Note this passes against the older directory comparison too - it is a behavioural
        // assertion, not a regression guard. See the sidecar-cleanup note in AudiobookService.
        SetupUpdateAudiobookTest();

        var author = new Person("Same Author");
        var narrator = new Person("A Narrator");
        // Same author/year, different book name -> same directory tree root, different leaf file.
        var oldFilePath = Path.Combine(_libraryPath, "Same Author", "2020 - Old Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath);
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var updateDto = new Audiobook(new List<Person> { author }, "New Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Description = "A description",
            Narrators = new List<Person> { narrator },
        };

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "New Name", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Description = "A description",
                Narrators = new List<Person> { narrator },
            });

        var expectedNewPath = _service.GenerateLibraryPath(
            new Audiobook(new List<Person> { author }, "New Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0)));
        var newDirectory = Path.GetDirectoryName(expectedNewPath)!;
        Directory.CreateDirectory(newDirectory);

        var readerInNewDirectory = Path.Combine(newDirectory, "reader.txt");

        var result = await _service.UpdateAudiobook(1, updateDto);

        Assert.AreEqual(expectedNewPath, result.FileInfo.FullPath);
        Assert.IsTrue(File.Exists(expectedNewPath));
        Assert.IsTrue(
            File.Exists(readerInNewDirectory),
            "reader.txt written into the destination directory must survive the old directory's cleanup");
        Assert.AreEqual("A Narrator", File.ReadAllText(readerInNewDirectory));
    }

    [TestMethod]
    public async Task UpdateAudiobook_Relocation_PreservesCoverArtSidecarWhenM4bHasNoEmbeddedCover()
    {
        SetupUpdateAudiobookTest();

        var author = new Person("Same Author");
        var oldFilePath = Path.Combine(_libraryPath, "Same Author", "2020 - Old Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath);
        var oldDirectory = Path.GetDirectoryName(oldFilePath)!;
        var oldCover = Path.Combine(oldDirectory, "cover.jpg");
        await File.WriteAllBytesAsync(oldCover, new byte[] { 0xFF, 0xD8 });

        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var updateDto = new Audiobook(new List<Person> { author }, "New Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));

        // Returns parsed audiobook WITHOUT embedded cover
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "New Name", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000)));

        var expectedNewPath = _service.GenerateLibraryPath(
            new Audiobook(new List<Person> { author }, "New Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0)));
        var newDirectory = Path.GetDirectoryName(expectedNewPath)!;

        var result = await _service.UpdateAudiobook(1, updateDto);

        var newCover = Path.Combine(newDirectory, "cover.jpg");
        Assert.IsTrue(File.Exists(newCover), "cover.jpg must be preserved in the new directory");
        Assert.IsFalse(File.Exists(oldCover), "old cover.jpg must be cleaned up from old directory");
        Assert.AreEqual(newCover, result.CoverFilePath);
    }

    // The other half of the sidecar contract: WriteMetadata owns these files, so a field that is
    // now empty removes its sidecar. Previously the write was simply skipped, leaving the value
    // the book had before the edit on disk - and Audiobookshelf reads desc.txt in preference to
    // the m4b's own Description tag, so the cleared field kept being served indefinitely.
    [TestMethod]
    public async Task UpdateAudiobook_ClearingDescriptionAndNarrators_RemovesTheirSidecars()
    {
        SetupUpdateAudiobookTest();

        // The book stays where it is, so nothing but the sidecar rules can explain the deletions.
        var author = new Person("Same Author");
        var probe = new Audiobook(new List<Person> { author }, "A Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));
        var expectedPath = _service.GenerateLibraryPath(probe);
        var bookDirectory = Path.GetDirectoryName(expectedPath)!;
        Directory.CreateDirectory(bookDirectory);
        File.WriteAllText(expectedPath, "fake audio");

        var existing = CreateExistingDbAudiobook(1, expectedPath);
        existing.BookName = "A Book";
        existing.Authors = new List<DbPerson> { new DbPerson(1, "Same Author") };
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var updateDto = new Audiobook(new List<Person> { author }, "A Book", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0))
        {
            Description = null,
            Narrators = new List<Person>(),
        };

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "A Book", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Description = null,
                Narrators = new List<Person>(),
            });

        var descPath = Path.Combine(bookDirectory, "desc.txt");
        var readerPath = Path.Combine(bookDirectory, "reader.txt");
        File.WriteAllText(descPath, "the description this book used to have");
        File.WriteAllText(readerPath, "Narrator Who Left");

        await _service.UpdateAudiobook(1, updateDto);

        Assert.IsFalse(File.Exists(descPath), "desc.txt should be removed once the Description is cleared");
        Assert.IsFalse(File.Exists(readerPath), "reader.txt should be removed once the Narrators are cleared");
        Assert.IsTrue(File.Exists(Path.Combine(bookDirectory, "metadata.opf")));
    }

    [TestMethod]
    public async Task UpdateAudiobook_VerifiesTheTagRoundTripWithoutDecodingTheCover()
    {
        // The round-trip check compares text tags only, so encoding the cover it just wrote is
        // wasted allocation on every single save.
        SetupUpdateAudiobookTest();

        var filePath = Path.Combine(_libraryPath, "Old Author", "2020 - Old Book Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, filePath);
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        var author = new Person("Old Author");
        var updateDto = new Audiobook(new List<Person> { author }, "Old Book Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "Old Book Name", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000)));

        await _service.UpdateAudiobook(1, updateDto);

        // The verification parse asks for no cover data; the post-relocation reparse (which
        // feeds WriteCover) still does.
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), false), Times.Once);
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), true), Times.Once);
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

        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "Old Book Name", 2020, new AudiobookFileInfo(fi.FullName, fi.Name, 1000)));

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
    public async Task UpdateAudiobook_TagWriteThrowsUnauthorizedAccess_WrapsWithActionableMessage()
    {
        // Regression test: a raw UnauthorizedAccessException (e.g. from a file owned by a
        // different user/group than the app runs as - common on unRAID/Linux setups) must not
        // reach the caller as opaque .NET exception text; it should be wrapped with guidance
        // pointing at the permission mismatch.
        SetupUpdateAudiobookTest();

        var oldFilePath = Path.Combine(_libraryPath, "Old Author", "2020 - Old Book Name", "book.m4b");
        var existing = CreateExistingDbAudiobook(1, oldFilePath);
        SetupCommonRepositoryMocks(1, existing);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()))
            .Throws(new UnauthorizedAccessException("Access to the path is denied."));

        var updateDto = new Audiobook(new List<Person> { new Person("Old Author") }, "Old Book Name", 2020, new AudiobookFileInfo("/unused/unused.m4b", "unused.m4b", 0));

        var ex = await Assert.ThrowsExactlyAsync<Exception>(() => _service.UpdateAudiobook(1, updateDto));

        Assert.IsInstanceOfType<UnauthorizedAccessException>(ex.InnerException);
        StringAssert.Contains(ex.Message, "Permission denied");
        StringAssert.Contains(ex.Message, oldFilePath);
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
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { newAuthor }, "New Book Name", 2024, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Narrators = new List<Person> { new Person("Stale Old Narrator") }
            });

        await Assert.ThrowsExactlyAsync<Exception>(() => _service.UpdateAudiobook(1, updateDto));

        Assert.IsTrue(File.Exists(oldFilePath), "File must not have been relocated since the round-trip check failed before relocation");
        _audiobookRepository.Verify(r => r.UpdateAudiobookAsync(It.IsAny<DbAudiobook>()), Times.Never);
    }

    #endregion

    #region OrganizeAudiobook

    [TestMethod]
    public async Task OrganizeAudiobook_MovesFileWritesSidecarsAndInsertsAudiobook_ReportsProgressPhasesInOrder()
    {
        SetupUpdateAudiobookTest();

        var importPath = Path.Combine(_testRoot, "import", "book.m4b");
        Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
        File.WriteAllText(importPath, "original m4b content");

        var author = new Person("New Author");
        var audiobook = new Audiobook(new List<Person> { author }, "New Book", 2024, new AudiobookFileInfo(importPath, Path.GetFileName(importPath), 1000))
        {
            Narrators = new List<Person> { new Person("New Narrator") }
        };

        var expectedPath = _service.GenerateLibraryPath(audiobook);

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "New Book", 2024, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Narrators = new List<Person> { new Person("New Narrator") }
            });

        _personRepository.Setup(r => r.GetOrCreatePersons(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> names) => names.Distinct().ToDictionary(n => n, n => new DbPerson(1, n)));
        _genreRepository.Setup(r => r.GetOrCreateGenres(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, DbGenre>());
        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 5;
                return db;
            });

        var progressLog = new List<(string Message, int Progress)>();
        Task ProgressAction(string message, int progress)
        {
            progressLog.Add((message, progress));
            return Task.CompletedTask;
        }

        var result = await _service.OrganizeAudiobook(audiobook, ProgressAction);

        Assert.AreEqual(expectedPath, result.FileInfo.FullPath);
        Assert.IsTrue(File.Exists(expectedPath), "File should have been relocated into the library");
        Assert.IsFalse(File.Exists(importPath), "File should no longer exist at the import path");
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(importPath)), "Now-empty import directory should be removed");

        _audiobookRepository.Verify(r => r.InsertAudiobook(It.Is<DbAudiobook>(db => db.FileInfoFullPath == expectedPath)), Times.Once);

        // Asserts the shared pipeline reports the same phase names/percentages OrganizeAudiobook
        // always has, so a future divergence from UpdateAudiobook's phases would fail this test.
        CollectionAssert.AreEqual(
            new[]
            {
                ("Started", 0),
                ("Saved tags", 70),
                ("Generated new path, relocating", 75),
                ("Relocated", 80),
                ("Reparsed", 85),
                ("Written metadata files", 90),
                ("Written cover", 95),
                ("Done", 100)
            },
            progressLog);
    }

    [TestMethod]
    public async Task OrganizeAudiobook_TagsDoNotRoundTripAfterSave_ThrowsAndDoesNotInsertOrRelocate()
    {
        // Regression test: the shared pipeline's tag round-trip verification (previously present
        // only in UpdateAudiobook - see the identically-named UpdateAudiobook test above) must also
        // guard OrganizeAudiobook now that both flows share it, so an organize can never silently
        // insert a DB record whose tags don't match what's really on disk.
        SetupUpdateAudiobookTest();

        var importPath = Path.Combine(_testRoot, "import", "book.m4b");
        Directory.CreateDirectory(Path.GetDirectoryName(importPath)!);
        File.WriteAllText(importPath, "original m4b content");

        var author = new Person("New Author");
        var audiobook = new Audiobook(new List<Person> { author }, "New Book", 2024, new AudiobookFileInfo(importPath, Path.GetFileName(importPath), 1000))
        {
            Narrators = new List<Person> { new Person("New Narrator") }
        };

        _tagHandler.Setup(t => t.SaveAudiobookTagsToFile(It.IsAny<Audiobook>(), It.IsAny<Action<float>?>()));

        // Simulate the tag write not actually persisting the narrator, even though
        // SaveAudiobookTagsToFile reported success.
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns((FileInfo fi, bool _) => new Audiobook(new List<Person> { author }, "New Book", 2024, new AudiobookFileInfo(fi.FullName, fi.Name, 1000))
            {
                Narrators = new List<Person> { new Person("Stale Narrator") }
            });

        await Assert.ThrowsExactlyAsync<Exception>(() => _service.OrganizeAudiobook(audiobook, (_, _) => Task.CompletedTask));

        Assert.IsTrue(File.Exists(importPath), "File must not have been relocated since the round-trip check failed before relocation");
        _audiobookRepository.Verify(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()), Times.Never);
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
            year: 2016, description: null, copyright: null, publisher: null, language: null, rating: null,
            asin: null, www: null, coverFilePath: null, durationInSeconds: 39600,
            fileInfoFullPath: targetPath, fileInfoFileName: Path.GetFileName(targetPath), fileInfoSizeInBytes: 598_000_000);

        _audiobookRepository.Setup(r => r.GetByFullPathAsync(targetPath, It.IsAny<Func<string, string, bool>?>())).ReturnsAsync(existingDbBook);

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

        _audiobookRepository.Setup(r => r.GetByFullPathAsync(targetPath, It.IsAny<Func<string, string, bool>?>())).ReturnsAsync((DbAudiobook?)null);

        var result = await _service.CheckTargetPathCollision(book);

        Assert.IsTrue(result.Exists);
        Assert.IsNull(result.ExistingAudiobookId);
        Assert.AreEqual(content.Length, result.ExistingSizeInBytes);
        Assert.IsNull(result.ExistingDurationInSeconds);
    }

    #endregion

    #region ReplaceExisting and DeleteAudiobook

    [TestMethod]
    public async Task OrganizeAudiobook_TargetExistsAndReplaceExistingFalse_ThrowsException()
    {
        SetupUpdateAudiobookTest();
        try
        {
            var book = MakeAudiobookForCollisionCheck();
            var targetPath = _service.GenerateLibraryPath(book);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, "existing target");

            book.ReplaceExisting = false;

            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), false)).Returns(book);

            var ex = await Assert.ThrowsExactlyAsync<Exception>(() =>
                _service.OrganizeAudiobook(book, (_, _) => Task.CompletedTask));

            StringAssert.Contains(ex.Message, "already exists");
        }
        finally
        {
            CleanupTestRoot(_testRoot);
        }
    }

    [TestMethod]
    public async Task OrganizeAudiobook_TargetExistsAndReplaceExistingTrue_OverwritesAndDeletesOldEntity()
    {
        SetupUpdateAudiobookTest();
        try
        {
            var sourceDir = Path.Combine(_testRoot, "import");
            Directory.CreateDirectory(sourceDir);
            var sourcePath = Path.Combine(sourceDir, "book.m4b");
            await File.WriteAllTextAsync(sourcePath, "new source content");

            var book = MakeAudiobookForCollisionCheck(sourcePath: sourcePath);
            var targetPath = _service.GenerateLibraryPath(book);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, "existing target");

            book.ReplaceExisting = true;

            var existingDbBook = new DbAudiobook(42, "Old Book", null, null, null, 2020, null, null, null, null, null, null, null, null, null, targetPath, "book.m4b", 5000);
            _audiobookRepository.Setup(r => r.GetByFullPathAsync(targetPath, It.IsAny<Func<string, string, bool>?>())).ReturnsAsync(existingDbBook);
            _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns((FileInfo fi, bool _) =>
            {
                var copy = MakeAudiobookForCollisionCheck(sourcePath: fi.FullName);
                copy.FileInfo = new AudiobookFileInfo(fi.FullName, fi.Name, fi.Length);
                return copy;
            });
            _personRepository.Setup(r => r.GetOrCreatePersons(It.IsAny<IEnumerable<string>>())).ReturnsAsync(new Dictionary<string, DbPerson>
            {
                ["Adrian Tchaikovsky"] = new DbPerson(1, "Adrian Tchaikovsky")
            });
            _genreRepository.Setup(r => r.GetOrCreateGenres(It.IsAny<IEnumerable<string>>())).ReturnsAsync(new Dictionary<string, DbGenre>());
            _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>())).ReturnsAsync(existingDbBook);

            await _service.OrganizeAudiobook(book, (_, _) => Task.CompletedTask);

            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(42), Times.Once);
            _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(42), Times.Once);
            Assert.IsTrue(File.Exists(targetPath));
        }
        finally
        {
            CleanupTestRoot(_testRoot);
        }
    }

    [TestMethod]
    public async Task DeleteAudiobook_RemovesEntityIssuesAndFiles()
    {
        SetupUpdateAudiobookTest();
        try
        {
            var bookDir = Path.Combine(_libraryPath, "Author", "2024 - Book");
            Directory.CreateDirectory(bookDir);
            var filePath = Path.Combine(bookDir, "book.m4b");
            await File.WriteAllTextAsync(filePath, "audio content");

            var dbBook = new DbAudiobook(10, "Book", null, null, null, 2024, null, null, null, null, null, null, null, null, null, filePath, "book.m4b", 100);
            _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(10)).ReturnsAsync(dbBook);

            await _service.DeleteAudiobook(10);

            _issueRepository.Verify(r => r.DeleteByAudiobookIdAsync(10), Times.Once);
            _audiobookRepository.Verify(r => r.DeleteAudiobookAsync(10), Times.Once);
            Assert.IsFalse(File.Exists(filePath));
            Assert.IsFalse(Directory.Exists(bookDir));

            _logger.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Deleting audiobook 10")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            CleanupTestRoot(_testRoot);
        }
    }

    [TestMethod]
    public async Task InsertAudiobook_LogsCreation()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author1") },
            "Logged Book",
            2024,
            new AudiobookFileInfo("/path/logged.m4b", "logged.m4b", 1000));

        _personRepository.Setup(r => r.GetOrCreatePersons(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, DbPerson> { ["Author1"] = new DbPerson(1, "Author1") });
        _genreRepository.Setup(r => r.GetOrCreateGenres(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, DbGenre>());

        _audiobookRepository.Setup(r => r.InsertAudiobook(It.IsAny<DbAudiobook>()))
            .ReturnsAsync((DbAudiobook db) =>
            {
                db.Id = 77;
                return db;
            });

        var result = await _service.InsertAudiobook(audiobook);

        Assert.AreEqual(77, result.Id);
        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Added audiobook 77")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}
