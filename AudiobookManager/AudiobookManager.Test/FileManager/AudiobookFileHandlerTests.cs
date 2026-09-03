using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;
[TestClass]
public class AudiobookFileHandlerTests
{
    private readonly IAudiobookFileHandler _fileHandler = new AudiobookFileHandler(new FileOperations());

    [TestMethod]
    public void GetSafeCombinedPath_Test()
    {
        var pathParts = new List<string> { "test", "test2" };

        var result = AudiobookFileHandler.GetSafeCombinedPath(pathParts);

        Assert.AreEqual($"{pathParts[0]}{AudiobookFileHandler.GetDirectorySeparator()}{pathParts[1]}", result);
    }

    [TestMethod]
    public void GetSafeCompletePath_ReplacesEveryInvalidPathCharacter()
    {
        var invalidChars = Path.GetInvalidPathChars();
        if (invalidChars.Length == 0)
        {
            Assert.Inconclusive("This platform reports no invalid path characters.");
        }

        // Build a path with an invalid char between every valid segment, including adjacent
        // occurrences, so a single-pass replacement has to handle repeats and adjacency.
        var input = string.Join("", invalidChars.Select(c => $"a{c}{c}"));

        var result = AudiobookFileHandler.GetSafeCompletePath(input);

        Assert.IsFalse(result.Any(c => invalidChars.Contains(c)));
        Assert.AreEqual(new string('a', invalidChars.Length), result);
    }

    [TestMethod]
    public void PathsEqual_IdenticalPaths_ReturnsTrue()
    {
        var path = Path.Combine(Path.GetTempPath(), "Author", "Book.m4b");

        Assert.IsTrue(AudiobookFileHandler.PathsEqual(path, path));
    }

    [TestMethod]
    public void PathsEqual_DifferentPaths_ReturnsFalse()
    {
        var pathA = Path.Combine(Path.GetTempPath(), "Author", "Book.m4b");
        var pathB = Path.Combine(Path.GetTempPath(), "Author", "OtherBook.m4b");

        Assert.IsFalse(AudiobookFileHandler.PathsEqual(pathA, pathB));
    }

    [TestMethod]
    public void PathsEqual_SamePathDifferentCase_MatchesPlatformCaseSensitivity()
    {
        var lower = Path.Combine(Path.GetTempPath(), "author", "book.m4b");
        var upper = Path.Combine(Path.GetTempPath(), "AUTHOR", "BOOK.M4B");

        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.AreEqual(expected, AudiobookFileHandler.PathsEqual(lower, upper));
    }

    [TestMethod]
    public void PathStartsWith_PathUnderPrefix_ReturnsTrue()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "library");
        var path = Path.Combine(prefix, "Author", "Book.m4b");

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(path, prefix));
    }

    [TestMethod]
    public void PathStartsWith_PathNotUnderPrefix_ReturnsFalse()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "library");
        var path = Path.Combine(Path.GetTempPath(), "import", "Book.m4b");

        Assert.IsFalse(AudiobookFileHandler.PathStartsWith(path, prefix));
    }

    [TestMethod]
    public void PathStartsWith_PrefixDifferentCase_MatchesPlatformCaseSensitivity()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "library");
        var path = Path.Combine(Path.GetTempPath(), "LIBRARY", "Author", "Book.m4b");

        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.AreEqual(expected, AudiobookFileHandler.PathStartsWith(path, prefix));
    }

    [TestMethod]
    public void PathStartsWith_SiblingDirectoryWithPrefixAsNamePrefix_ReturnsFalse()
    {
        // Regression: PathStartsWith used a bare string StartsWith, so "/data/library-backup"
        // reported as being inside "/data/library". FileService.ValidatePathWithinAllowedBases
        // relies on this for access control in front of a recursive delete.
        var prefix = Path.Combine(Path.GetTempPath(), "library");
        var sibling = Path.Combine(Path.GetTempPath(), "library-backup", "Author", "Book.m4b");

        Assert.IsFalse(AudiobookFileHandler.PathStartsWith(sibling, prefix));
    }

    [TestMethod]
    public void PathStartsWith_PrefixItself_ReturnsTrue()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "library");

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(prefix, prefix));
    }

    [TestMethod]
    public void PathStartsWith_PrefixWithTrailingSeparator_StillMatchesPathsUnderneath()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "library") + Path.DirectorySeparatorChar;
        var path = Path.Combine(Path.GetTempPath(), "library", "Author", "Book.m4b");

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(path, prefix));
    }

    [TestMethod]
    public void PathStartsWith_RootPrefix_ReturnsTrue()
    {
        // A root base path is degenerate but legal (AudiobookLibraryPath is an env var). The
        // boundary check must not reject everything under it: GetFullPath leaves the trailing
        // separator on a root, so naively appending another one would never match.
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var path = Path.Combine(Path.GetTempPath(), "library", "Book.m4b");

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(path, root));
    }

    [TestMethod]
    public void PathStartsWith_PrefixWithTrailingSeparator_StillRejectsASibling()
    {
        // The trailing separator on the prefix must not be double-counted into the boundary.
        var prefix = Path.Combine(Path.GetTempPath(), "library") + Path.DirectorySeparatorChar;
        var sibling = Path.Combine(Path.GetTempPath(), "librarything", "Book.m4b");

        Assert.IsFalse(AudiobookFileHandler.PathStartsWith(sibling, prefix));
    }

    [TestMethod]
    public void PathStartsWith_PathIsThePrefixWithATrailingSeparator_ReturnsTrue()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "library");
        var path = prefix + Path.DirectorySeparatorChar;

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(path, prefix));
    }

    [TestMethod]
    public void PathComparer_MatchesPlatformCaseSensitivity()
    {
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.AreEqual(expected, AudiobookFileHandler.PathComparer.Equals("/library/a.m4b", "/LIBRARY/A.M4B"));
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithSeries_IncludesSeriesDirectory()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Brandon Sanderson") },
            "The Way of Kings",
            2010,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "The Stormlight Archive",
            SeriesPart = "1"
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        Assert.IsTrue(result.Contains($"Brandon Sanderson{sep}The Stormlight Archive{sep}"));
        Assert.IsTrue(result.Contains("Book 01 - "));
        Assert.IsTrue(result.Contains("The Way of Kings"));
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithoutSeries_NoSeriesDirectory()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author Name") },
            "Standalone Title",
            2023,
            new AudiobookFileInfo("/import/standalone.m4b", "standalone.m4b", 1000));

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        Assert.IsTrue(result.Contains($"Author Name{sep}2023 - Standalone Title"));
        // No series directory — path should be Author/Year - Title/filename only
        var parts = result.Split(sep);
        Assert.AreEqual(3, parts.Length, $"Expected 3 path segments (author/folder/file), got: {result}");
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithSeriesPart_PadsPartNumber()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author") },
            "Book Title",
            2020,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "My Series",
            SeriesPart = "3"
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        Assert.IsTrue(result.Contains("Book 03 - "));
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithSeriesAndPart_FullPathCorrect()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Brandon Sanderson") },
            "The Dark Talent",
            2016,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "Alcatraz vs. the Evil Librarians",
            SeriesPart = "5"
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        var expected = string.Join(sep.ToString(), new[]
        {
            "Brandon Sanderson",
            "Alcatraz vs. the Evil Librarians",
            "Book 05 - 2016 - The Dark Talent",
            "Alcatraz vs. the Evil Librarians 05 - 2016 - The Dark Talent.m4b"
        });
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithSeriesNoSeriesPart_OmitsBookPrefix()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Brandon Sanderson") },
            "The Dark Talent",
            2016,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "Alcatraz vs. the Evil Librarians",
            SeriesPart = null
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        var expected = string.Join(sep.ToString(), new[]
        {
            "Brandon Sanderson",
            "Alcatraz vs. the Evil Librarians",
            "2016 - The Dark Talent",
            "Alcatraz vs. the Evil Librarians - 2016 - The Dark Talent.m4b"
        });
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithSeriesAndSubtitle_ExcludesSubtitleFromDirectory()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author Name") },
            "Book Title",
            2020,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "My Series",
            SeriesPart = "2",
            Subtitle = "A Subtitle"
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        Assert.IsTrue(result.Contains($"Book 02 - 2020 - Book Title{sep}"));
        Assert.IsFalse(result.Contains("A Subtitle"));
        Assert.IsTrue(result.EndsWith("My Series 02 - 2020 - Book Title.m4b"));
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithDecimalSeriesPart_PadsCorrectly()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author") },
            "Side Story",
            2021,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "Main Series",
            SeriesPart = "1.5"
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        Assert.IsTrue(result.Contains("Book 01.5 - "));
        Assert.IsTrue(result.Contains("Main Series 01.5 - "));
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_WithMultipleAuthors_JoinsAuthors()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author One"), new Person("Author Two") },
            "Collab Book",
            2022,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = "Shared Series",
            SeriesPart = "1"
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        Assert.IsTrue(result.StartsWith("Author One, Author Two"));
    }

    [TestMethod]
    [DataRow("..")]
    [DataRow(".")]
    [DataRow("...")]
    [DataRow("  ..  ")]
    [DataRow("")]
    [DataRow("   ")]
    public void GetSafeFileName_UnusableSegment_ReturnsPlaceholder(string segment)
    {
        var result = AudiobookFileHandler.GetSafeFileName(segment);

        Assert.AreEqual("Unknown", result);
    }

    [TestMethod]
    public void GetSafeFileName_NameContainingDots_IsKept()
    {
        // Only a segment that is *nothing but* dots is navigation; a real title that happens to
        // contain them must survive untouched.
        Assert.AreEqual("Star Wars Ep. II", AudiobookFileHandler.GetSafeFileName("Star Wars Ep. II"));
        Assert.AreEqual("2020 - ..", AudiobookFileHandler.GetSafeFileName("2020 - .."));
    }

    [TestMethod]
    public void GetSafeCombinedPath_EmptyPart_KeepsHierarchyLevel()
    {
        // An empty part used to leave the accumulator empty, which the old aggregate read as "no
        // segment yet" - so the level was dropped and the book landed one directory too high.
        var result = AudiobookFileHandler.GetSafeCombinedPath(new List<string> { "", "Series", "Book" });

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        Assert.AreEqual($"Unknown{sep}Series{sep}Book", result);
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_TraversalInTags_DoesNotEscape()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("..") },
            "..",
            2020,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000))
        {
            Series = ".."
        };

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        CollectionAssert.DoesNotContain(result.Split(sep), "..");
        Assert.IsTrue(result.StartsWith($"Unknown{sep}Unknown{sep}"), $"Unexpected path: {result}");
    }

    [TestMethod]
    public void GenerateRelativeAudiobookPath_SeparatorsInTags_DoNotCreateExtraLevels()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("../../etc") },
            "Book",
            2020,
            new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1000));

        var result = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);

        var sep = AudiobookFileHandler.GetDirectorySeparator();
        var segments = result.Split(sep);

        // Author / "Year - Book" / file - the separators in the tag must not add levels of their own.
        Assert.AreEqual(3, segments.Length, $"Expected author/folder/file, got: {result}");
        Assert.AreEqual(".._.._etc", segments[0]);
    }

    [TestMethod]
    public void JoinLibraryPath_PathInsideRoot_IsReturned()
    {
        var result = AudiobookFileHandler.JoinLibraryPath("/library", "Author/2020 - Book/book.m4b");

        Assert.IsTrue(AudiobookFileHandler.PathStartsWith(result, "/library"));
        Assert.IsTrue(result.Contains("2020 - Book"));
    }

    [TestMethod]
    public void JoinLibraryPath_RelativePathEscapingRoot_Throws()
    {
        // The backstop for a relative path that got past segment sanitization by some other route.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            AudiobookFileHandler.JoinLibraryPath("/library", "../../escaped.m4b"));

        StringAssert.Contains(ex.Message, "outside the library root");
    }

    [TestMethod]
    public void JoinLibraryPath_SiblingOfRoot_Throws()
    {
        // A boundary case a bare StartsWith would wrongly accept.
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AudiobookFileHandler.JoinLibraryPath("/library", "../library-backup/book.m4b"));
    }

    [TestMethod]
    public void WriteMetadata_WritesDescAndReaderFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            File.WriteAllText(tempFile, "fake");

            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Test Book",
                2024,
                new AudiobookFileInfo(tempFile, "test.m4b", 100))
            {
                Description = "A wonderful description",
                Narrators = new List<Person> { new Person("Narrator One"), new Person("Narrator Two") }
            };

            _fileHandler.WriteMetadata(audiobook);

            var descPath = Path.Combine(tempDir, "desc.txt");
            var readerPath = Path.Combine(tempDir, "reader.txt");
            var opfPath = Path.Combine(tempDir, "metadata.opf");

            Assert.IsTrue(File.Exists(descPath));
            Assert.IsTrue(File.Exists(readerPath));
            Assert.AreEqual("A wonderful description", File.ReadAllText(descPath));
            Assert.AreEqual("Narrator One, Narrator Two", File.ReadAllText(readerPath));

            // WriteMetadata always writes metadata.opf alongside desc.txt/reader.txt, unlike those
            // two which are conditional on Description/Narrators being set.
            Assert.IsTrue(File.Exists(opfPath));
            Assert.AreEqual(AudiobookFileHandler.BuildOpfContent(audiobook), File.ReadAllText(opfPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // Regression: clearing a book's description left the previous desc.txt on disk. WriteMetadata
    // simply skipped the file when the field was empty, and the consistency check only looked at
    // desc.txt when the tag was set - so the stale text survived every save and every check, and
    // Audiobookshelf reads that file in preference to the m4b's own tag.
    [TestMethod]
    public void WriteMetadata_EmptyDescriptionAndNoNarrators_RemovesStaleDescAndReaderFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            File.WriteAllText(tempFile, "fake");

            var descPath = Path.Combine(tempDir, "desc.txt");
            var readerPath = Path.Combine(tempDir, "reader.txt");
            File.WriteAllText(descPath, "the description this book used to have");
            File.WriteAllText(readerPath, "Narrator Who Left");

            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Test Book",
                2024,
                new AudiobookFileInfo(tempFile, "test.m4b", 100))
            {
                Description = null,
                Narrators = new List<Person>()
            };

            _fileHandler.WriteMetadata(audiobook);

            Assert.IsFalse(File.Exists(descPath));
            Assert.IsFalse(File.Exists(readerPath));
            // metadata.opf is unconditional and must still be written.
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "metadata.opf")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void WriteMetadata_NoStaleSidecars_LeavesTheDirectoryUntouched()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            File.WriteAllText(tempFile, "fake");

            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Test Book",
                2024,
                new AudiobookFileInfo(tempFile, "test.m4b", 100));

            _fileHandler.WriteMetadata(audiobook);

            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "desc.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "reader.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "metadata.opf")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // Regression: switching a book's cover from PNG to JPEG (or back) left both files behind,
    // so which image a reader picked up was undefined.
    [TestMethod]
    public void WriteCover_ReplacingACoverOfADifferentType_RemovesTheOtherCoverFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            File.WriteAllText(tempFile, "fake");

            var pngPath = Path.Combine(tempDir, "cover.png");
            File.WriteAllBytes(pngPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Test Book",
                2024,
                new AudiobookFileInfo(tempFile, "test.m4b", 100))
            {
                Cover = new AudiobookImage(Convert.ToBase64String(jpegBytes), "image/jpeg")
            };

            var result = _fileHandler.WriteCover(audiobook);

            Assert.IsNotNull(result);
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "cover.jpg")));
            Assert.IsFalse(File.Exists(pngPath));
            CollectionAssert.AreEqual(jpegBytes, File.ReadAllBytes(result!));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void BuildOpfContent_IncludesAuthorsNarratorsSeriesGenresAndAsin()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Brandon Sanderson") },
            "The Way of Kings",
            2010,
            new AudiobookFileInfo("/library/book.m4b", "book.m4b", 100))
        {
            Narrators = new List<Person> { new Person("Michael Kramer") },
            Series = "The Stormlight Archive",
            SeriesPart = "1",
            Genres = new List<string> { "Fantasy", "Adventure" },
            Description = "An epic fantasy novel",
            Publisher = "Tor Books",
            Language = "English",
            Asin = "B0041D2NGE"
        };

        var opf = AudiobookFileHandler.BuildOpfContent(audiobook);

        Assert.IsTrue(opf.Contains("<dc:title>The Way of Kings</dc:title>"));
        Assert.IsTrue(opf.Contains(">Brandon Sanderson<"));
        Assert.IsTrue(opf.Contains(">Michael Kramer<"));
        Assert.IsTrue(opf.Contains(">An epic fantasy novel<"));
        Assert.IsTrue(opf.Contains(">Tor Books<"));
        Assert.IsTrue(opf.Contains(">2010<"));
        Assert.IsTrue(opf.Contains(">English<"));
        Assert.IsTrue(opf.Contains(">Fantasy<"));
        Assert.IsTrue(opf.Contains(">Adventure<"));
        Assert.IsTrue(opf.Contains(">B0041D2NGE<"));
        Assert.IsTrue(opf.Contains("name=\"calibre:series\""));
        Assert.IsTrue(opf.Contains("content=\"The Stormlight Archive\""));
        Assert.IsTrue(opf.Contains("name=\"calibre:series_index\""));
        Assert.IsTrue(opf.Contains("content=\"1\""));
    }

    [TestMethod]
    public void BuildOpfContent_MinimalBook_OmitsEmptyOptionalFields()
    {
        var audiobook = new Audiobook(
            new List<Person>(),
            "Bare Book",
            null,
            new AudiobookFileInfo("/library/bare.m4b", "bare.m4b", 100));

        var opf = AudiobookFileHandler.BuildOpfContent(audiobook);

        Assert.IsFalse(opf.Contains("calibre:series"));
        Assert.IsFalse(opf.Contains("dc:contributor"));
        Assert.IsFalse(opf.Contains("dc:creator"));
        Assert.IsFalse(opf.Contains("dc:identifier"));
    }

    [TestMethod]
    public void BuildOpfContent_CalledTwiceWithSameData_ProducesIdenticalContent()
    {
        // The consistency checker compares BuildOpfContent's output against what's on disk, so
        // this has to be deterministic for the same input or every book would falsely drift.
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author") },
            "Test Book",
            2024,
            new AudiobookFileInfo("/library/book.m4b", "book.m4b", 100))
        {
            Genres = new List<string> { "Fiction" }
        };

        var first = AudiobookFileHandler.BuildOpfContent(audiobook);
        var second = AudiobookFileHandler.BuildOpfContent(audiobook);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void WriteCover_WritesCoverFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            File.WriteAllText(tempFile, "fake");

            // Create a small valid base64 payload
            var coverBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
            var base64 = Convert.ToBase64String(coverBytes);

            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Test Book",
                2024,
                new AudiobookFileInfo(tempFile, "test.m4b", 100))
            {
                Cover = new AudiobookImage(base64, "image/jpeg")
            };

            var result = _fileHandler.WriteCover(audiobook);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.EndsWith("cover.jpg"));
            Assert.IsTrue(File.Exists(result));

            var writtenBytes = File.ReadAllBytes(result);
            CollectionAssert.AreEqual(coverBytes, writtenBytes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void WriteCover_NoCoverOnAudiobook_ReturnsNullAndWritesNoFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "test.m4b");
            File.WriteAllText(tempFile, "fake");

            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Test Book",
                2024,
                new AudiobookFileInfo(tempFile, "test.m4b", 100));

            var result = _fileHandler.WriteCover(audiobook);

            Assert.IsNull(result);
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "cover.jpg")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "cover.png")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void GetExistingCoverPath_JpgExists_ReturnsItsPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var jpgPath = Path.Combine(tempDir, "cover.jpg");
            File.WriteAllBytes(jpgPath, new byte[] { 0xFF, 0xD8, 0xFF });

            var result = _fileHandler.GetExistingCoverPath(tempDir, cleanupDuplicate: false);

            Assert.AreEqual(jpgPath, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void GetExistingCoverPath_NoCover_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = _fileHandler.GetExistingCoverPath(tempDir, cleanupDuplicate: false);

            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // Regression: a passive lookup (e.g. previewing a not-yet-imported discovered file's cover)
    // must never mutate the directory it is only looking at - only WriteCover, which owns the
    // book being saved, is allowed to resolve a stale duplicate by deleting one of the two files.
    [TestMethod]
    public void GetExistingCoverPath_CleanupDuplicateFalse_BothJpgAndPngExist_DeletesNeitherFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var jpgPath = Path.Combine(tempDir, "cover.jpg");
            var pngPath = Path.Combine(tempDir, "cover.png");
            File.WriteAllBytes(jpgPath, new byte[] { 0xFF, 0xD8, 0xFF });
            File.WriteAllBytes(pngPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var result = _fileHandler.GetExistingCoverPath(tempDir, cleanupDuplicate: false);

            Assert.AreEqual(jpgPath, result);
            Assert.IsTrue(File.Exists(jpgPath));
            Assert.IsTrue(File.Exists(pngPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void RemoveSidecarFiles_RemovesKnownSidecarFiles_LeavesOthersAlone()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "desc.txt"), "desc");
            File.WriteAllText(Path.Combine(tempDir, "reader.txt"), "reader");
            File.WriteAllBytes(Path.Combine(tempDir, "cover.jpg"), new byte[] { 0xFF, 0xD8 });
            File.WriteAllText(Path.Combine(tempDir, "metadata.opf"), "<package/>");
            File.WriteAllText(Path.Combine(tempDir, "keep.me"), "unrelated file");

            _fileHandler.RemoveSidecarFiles(tempDir);

            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "desc.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "reader.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "cover.jpg")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "metadata.opf")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "keep.me")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void RemoveSidecarFiles_NonExistentDirectory_DoesNotThrow()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        _fileHandler.RemoveSidecarFiles(missingDir);
    }
}
