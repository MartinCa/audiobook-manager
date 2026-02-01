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
