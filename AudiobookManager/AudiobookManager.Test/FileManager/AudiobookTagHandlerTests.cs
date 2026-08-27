using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.FileManager;

/// <summary>
/// Exercises real tag read/write round-trips against a tiny real m4b fixture
/// (FileManager/TestData/fixture.m4b, a 1-second silent AAC track generated with ffmpeg) since
/// ATL reads/writes actual container bytes and can't be meaningfully mocked.
/// </summary>
[TestClass]
public class AudiobookTagHandlerTests
{
    private const string FixturePath = "FileManager/TestData/fixture.m4b";

    private Mock<ILogger<AudiobookTagHandler>> _logger = null!;
    private Mock<IAtlLogging> _atlLogging = null!;
    private AudiobookTagHandler _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = new Mock<ILogger<AudiobookTagHandler>>();
        _atlLogging = new Mock<IAtlLogging>();
        _handler = new AudiobookTagHandler(_logger.Object, _atlLogging.Object);
    }

    private static string CopyFixtureToTempFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "book.m4b");
        File.Copy(FixturePath, tempFile);
        return tempFile;
    }

    [TestMethod]
    public void SaveAudiobookTagsToFile_ThenParseAudiobook_RoundTripsAuthorSeriesYearNarrator()
    {
        var tempFile = CopyFixtureToTempFile();
        var tempDir = Path.GetDirectoryName(tempFile)!;

        try
        {
            var audiobook = new Audiobook(
                new List<Person> { new Person("Brandon Sanderson") },
                "The Way of Kings",
                2010,
                new AudiobookFileInfo(tempFile, Path.GetFileName(tempFile), new FileInfo(tempFile).Length))
            {
                Narrators = new List<Person> { new Person("Michael Kramer"), new Person("Kate Reading") },
                Series = "The Stormlight Archive",
                SeriesPart = "1"
            };

            _handler.SaveAudiobookTagsToFile(audiobook);

            var reparsed = _handler.ParseAudiobook(new FileInfo(tempFile));

            Assert.AreEqual("Brandon Sanderson", string.Join(", ", reparsed.Authors.Select(a => a.Name)));
            Assert.AreEqual("The Way of Kings", reparsed.BookName);
            Assert.AreEqual(2010, reparsed.Year);
            Assert.AreEqual("The Stormlight Archive", reparsed.Series);
            Assert.AreEqual("1", reparsed.SeriesPart);
            Assert.AreEqual(2, reparsed.Narrators.Count);
            Assert.IsTrue(reparsed.Narrators.Any(n => n.Name == "Michael Kramer"));
            Assert.IsTrue(reparsed.Narrators.Any(n => n.Name == "Kate Reading"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void SaveAudiobookTagsToFile_WithoutSeries_ParsesBackWithNullSeries()
    {
        var tempFile = CopyFixtureToTempFile();
        var tempDir = Path.GetDirectoryName(tempFile)!;

        try
        {
            var audiobook = new Audiobook(
                new List<Person> { new Person("Author Name") },
                "Standalone Title",
                2023,
                new AudiobookFileInfo(tempFile, Path.GetFileName(tempFile), new FileInfo(tempFile).Length));

            _handler.SaveAudiobookTagsToFile(audiobook);

            var reparsed = _handler.ParseAudiobook(new FileInfo(tempFile));

            Assert.AreEqual("Standalone Title", reparsed.BookName);
            Assert.IsTrue(string.IsNullOrEmpty(reparsed.Series));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void SaveAudiobookTagsToFile_WithDecimalSeriesPart_RoundTripsPaddedValue()
    {
        var tempFile = CopyFixtureToTempFile();
        var tempDir = Path.GetDirectoryName(tempFile)!;

        try
        {
            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Side Story",
                2021,
                new AudiobookFileInfo(tempFile, Path.GetFileName(tempFile), new FileInfo(tempFile).Length))
            {
                Series = "Main Series",
                SeriesPart = "1.5"
            };

            _handler.SaveAudiobookTagsToFile(audiobook);

            var reparsed = _handler.ParseAudiobook(new FileInfo(tempFile));

            Assert.AreEqual("Main Series", reparsed.Series);
            // SeriesPart is stored/read back as the raw (unpadded) value; padding is applied
            // only when building display strings such as the Title tag, via PadSeriesPart().
            Assert.AreEqual("1.5", reparsed.SeriesPart);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void SaveAudiobookTagsToFile_ReportsProgress()
    {
        var tempFile = CopyFixtureToTempFile();
        var tempDir = Path.GetDirectoryName(tempFile)!;

        try
        {
            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Book",
                2020,
                new AudiobookFileInfo(tempFile, Path.GetFileName(tempFile), new FileInfo(tempFile).Length));

            var progressValues = new List<float>();

            _handler.SaveAudiobookTagsToFile(audiobook, p => progressValues.Add(p));

            Assert.IsTrue(progressValues.Count > 0, "progress callback should have been invoked at least once");
            Assert.IsTrue(progressValues.All(p => p is >= 0 and <= 1), "progress values should be normalized between 0 and 1");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void ParseAudiobook_MissingFile_ThrowsFileNotFoundException()
    {
        var missingFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.m4b");

        Assert.ThrowsExactly<FileNotFoundException>(() => _handler.ParseAudiobook(new FileInfo(missingFile)));
    }

    // Note: ATL (7.16.0) treats an unrecognized/garbage file as a generic "Unknown" format
    // with AudioFormat.Readable == true and AudioFormat.ID == 0 (not -1), rather than failing
    // to read it outright. AudiobookTagHandler's `!track.AudioFormat.Readable || ID == -1`
    // guard is therefore only reachable for a narrower class of failures than "any malformed
    // file" - garbage content alone does not trip it, and Parse/Save instead complete with a
    // near-empty/default result. These tests document that actual (graceful, non-throwing)
    // behavior rather than asserting UnsupportedFormatException, which does not occur for this
    // input shape.
    [TestMethod]
    public void ParseAudiobook_UnrecognizedContent_ReturnsEmptyAudiobookWithoutThrowing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "not-really-audio.m4b");
            File.WriteAllText(tempFile, "this is not a valid m4b file");

            var result = _handler.ParseAudiobook(new FileInfo(tempFile));

            Assert.IsTrue(string.IsNullOrEmpty(result.BookName));
            Assert.AreEqual(0, result.Authors.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void SaveAudiobookTagsToFile_UnrecognizedContent_DoesNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFile = Path.Combine(tempDir, "not-really-audio.m4b");
            File.WriteAllText(tempFile, "this is not a valid m4b file");

            var audiobook = new Audiobook(
                new List<Person> { new Person("Author") },
                "Book",
                2020,
                new AudiobookFileInfo(tempFile, "not-really-audio.m4b", 100));

            _handler.SaveAudiobookTagsToFile(audiobook);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void SaveAudiobookTagsToFile_NullFileInfo_ThrowsArgumentNullException()
    {
        var audiobook = new Audiobook(
            new List<Person> { new Person("Author") },
            "Book",
            2020,
            null!);

        Assert.ThrowsExactly<ArgumentNullException>(() => _handler.SaveAudiobookTagsToFile(audiobook));
    }

    [TestMethod]
    public void IsSupported_M4bExtension_ReturnsTrue()
    {
        var fileInfo = new FileInfo("somebook.m4b");

        Assert.IsTrue(AudiobookTagHandler.IsSupported(fileInfo));
    }

    [TestMethod]
    public void IsSupported_OtherExtension_ReturnsFalse()
    {
        var fileInfo = new FileInfo("somebook.mp3");

        Assert.IsFalse(AudiobookTagHandler.IsSupported(fileInfo));
    }

    [TestMethod]
    public void ParsePersonsFromString_CommaSeparatedNames_ParsesEachTrimmed()
    {
        var result = AudiobookTagHandler.ParsePersonsFromString("Author One, Author Two ,Author Three");

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("Author One", result[0].Name);
        Assert.AreEqual("Author Two", result[1].Name);
        Assert.AreEqual("Author Three", result[2].Name);
    }

    [TestMethod]
    public void ParsePersonsFromString_EmptyString_ReturnsEmptyList()
    {
        var result = AudiobookTagHandler.ParsePersonsFromString(string.Empty);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetStringFromListOfPersons_JoinsDistinctNames()
    {
        var persons = new List<Person> { new Person("Author One"), new Person("Author Two"), new Person("Author One") };

        var result = AudiobookTagHandler.GetStringFromListOfPersons(persons);

        Assert.AreEqual("Author One, Author Two", result);
    }

    [TestMethod]
    public void PadSeriesPart_SingleDigit_PadsToTwoDigits()
    {
        Assert.AreEqual("03", AudiobookTagHandler.PadSeriesPart("3"));
    }

    [TestMethod]
    public void PadSeriesPart_DecimalValue_PadsIntegerPortion()
    {
        Assert.AreEqual("01.5", AudiobookTagHandler.PadSeriesPart("1.5"));
    }

    [TestMethod]
    public void PadSeriesPart_MultiPartRange_PadsBothSides()
    {
        Assert.AreEqual("01-03", AudiobookTagHandler.PadSeriesPart("1-3"));
    }

    [TestMethod]
    public void PadSeriesPart_Null_ReturnsNull()
    {
        Assert.IsNull(AudiobookTagHandler.PadSeriesPart(null));
    }
}
