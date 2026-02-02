using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;
[TestClass]
public class AudiobookFileHandlerTests
{
    [TestMethod]
    public void GetSafeCombinedPath_Test()
    {
        var pathParts = new List<string> { "test", "test2" };

        var result = AudiobookFileHandler.GetSafeCombinedPath(pathParts);

        Assert.AreEqual($"{pathParts[0]}{AudiobookFileHandler.GetDirectorySeparator()}{pathParts[1]}", result);
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
    public void GenerateRelativeAudiobookPath_WithSeriesAndSubtitle_IncludesSubtitleInDirectory()
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
        // Directory should include subtitle, filename should not
        Assert.IsTrue(result.Contains($"Book 02 - 2020 - Book Title - A Subtitle{sep}"));
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

            AudiobookFileHandler.WriteMetadata(audiobook);

            var descPath = Path.Combine(tempDir, "desc.txt");
            var readerPath = Path.Combine(tempDir, "reader.txt");

            Assert.IsTrue(File.Exists(descPath));
            Assert.IsTrue(File.Exists(readerPath));
            Assert.AreEqual("A wonderful description", File.ReadAllText(descPath));
            Assert.AreEqual("Narrator One, Narrator Two", File.ReadAllText(readerPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
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

            var result = AudiobookFileHandler.WriteCover(audiobook);

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
}
