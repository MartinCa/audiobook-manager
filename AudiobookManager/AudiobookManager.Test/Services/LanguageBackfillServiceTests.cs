using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class LanguageBackfillServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private LanguageBackfillService _service = null!;
    private List<(string Message, int Scanned, int Total)> _progress = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _tagHandler = new Mock<IAudiobookTagHandler>();
        _progress = new List<(string, int, int)>();
        _service = new LanguageBackfillService(
            _audiobookRepository.Object,
            _tagHandler.Object,
            Mock.Of<ILogger<LanguageBackfillService>>());
    }

    private Task ProgressAction(string message, int scanned, int total)
    {
        _progress.Add((message, scanned, total));
        return Task.CompletedTask;
    }

    private static Audiobook ParsedWithLanguage(string? language) =>
        new(new List<Person> { new("An Author") }, "A Book", 2020, new AudiobookFileInfo("/library/a.m4b", "a.m4b", 1))
        {
            Language = language
        };

    /// <summary>
    /// Moq matches <c>ParseAudiobook(It.IsAny&lt;FileInfo&gt;())</c> as <c>(fileInfo, true)</c>,
    /// but the backfill deliberately passes <c>includeCoverData: false</c> - so the setup has to
    /// be widened with <c>It.IsAny&lt;bool&gt;()</c> or every call returns null instead.
    /// </summary>
    private void SetupParse(string path, Audiobook parsed) =>
        _tagHandler
            .Setup(t => t.ParseAudiobook(It.Is<FileInfo>(f => f.FullName == path), It.IsAny<bool>()))
            .Returns(parsed);

    [TestMethod]
    public async Task BackfillFromTagsAsync_StoresTheNormalizedCodeFromTheEmbeddedTag()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef>
            {
                new(1, "/library/english.m4b"),
                new(2, "/library/danish.m4b")
            });
        SetupParse("/library/english.m4b", ParsedWithLanguage("English"));
        SetupParse("/library/danish.m4b", ParsedWithLanguage("Dansk"));

        var result = await _service.BackfillFromTagsAsync(ProgressAction);

        _audiobookRepository.Verify(r => r.UpdateLanguageAsync(1, "en"), Times.Once);
        _audiobookRepository.Verify(r => r.UpdateLanguageAsync(2, "da"), Times.Once);
        Assert.AreEqual(new LanguageBackfillResult(2, 2, 0, 0), result);
    }

    [TestMethod]
    public async Task BackfillFromTagsAsync_LeavesABookWithNoLanguageTagEmpty()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef> { new(1, "/library/untagged.m4b") });
        SetupParse("/library/untagged.m4b", ParsedWithLanguage(null));

        var result = await _service.BackfillFromTagsAsync(ProgressAction);

        _audiobookRepository.Verify(r => r.UpdateLanguageAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual(new LanguageBackfillResult(1, 0, 1, 0), result);
    }

    [TestMethod]
    public async Task BackfillFromTagsAsync_SkipsALanguageTheLibraryDoesNotManage()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef> { new(1, "/library/german.m4b") });
        SetupParse("/library/german.m4b", ParsedWithLanguage("German"));

        var result = await _service.BackfillFromTagsAsync(ProgressAction);

        // Recording "German" would put a value in the database that the select cannot offer and
        // that no supported code matches; leaving it empty keeps the book in Missing Tags.
        _audiobookRepository.Verify(r => r.UpdateLanguageAsync(It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        Assert.AreEqual(new LanguageBackfillResult(1, 0, 1, 0), result);
    }

    [TestMethod]
    public async Task BackfillFromTagsAsync_CountsAnUnreadableFileAndCarriesOn()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef>
            {
                new(1, "/library/broken.m4b"),
                new(2, "/library/fine.m4b")
            });
        _tagHandler
            .Setup(t => t.ParseAudiobook(It.Is<FileInfo>(f => f.FullName == "/library/broken.m4b"), It.IsAny<bool>()))
            .Throws(new IOException("unreadable"));
        SetupParse("/library/fine.m4b", ParsedWithLanguage("eng"));

        var result = await _service.BackfillFromTagsAsync(ProgressAction);

        // The book after the failure still has to be processed - a library-wide pass must not
        // abort on one bad file.
        _audiobookRepository.Verify(r => r.UpdateLanguageAsync(2, "en"), Times.Once);
        Assert.AreEqual(new LanguageBackfillResult(2, 1, 0, 1), result);
    }

    [TestMethod]
    public async Task BackfillFromTagsAsync_ReadsWithoutCoverData()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef> { new(1, "/library/a.m4b") });
        SetupParse("/library/a.m4b", ParsedWithLanguage("en"));

        await _service.BackfillFromTagsAsync(ProgressAction);

        // Encoding the embedded cover plus a base64 string ~1.4x its size, once per book, for a
        // pass that reads a single tag.
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), false), Times.Once);
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), true), Times.Never);
    }

    [TestMethod]
    public async Task BackfillFromTagsAsync_ReportsFinalProgressAndDoesNothingForAnEmptyLibrary()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef>());

        var result = await _service.BackfillFromTagsAsync(ProgressAction);

        Assert.AreEqual(new LanguageBackfillResult(0, 0, 0, 0), result);
        Assert.AreEqual(0, _progress.Count);
        _tagHandler.Verify(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task BackfillFromTagsAsync_ReportsProgressForTheFinalBook()
    {
        _audiobookRepository
            .Setup(r => r.GetBooksMissingLanguageAsync())
            .ReturnsAsync(new List<AudiobookLanguageRef> { new(1, "/library/a.m4b"), new(2, "/library/b.m4b") });
        SetupParse("/library/a.m4b", ParsedWithLanguage("en"));
        SetupParse("/library/b.m4b", ParsedWithLanguage("da"));

        await _service.BackfillFromTagsAsync(ProgressAction);

        // Progress is batched, so only the last book reports - but it must, or the client's poll
        // would never see the run reach its total.
        Assert.AreEqual(1, _progress.Count);
        Assert.AreEqual(2, _progress[0].Scanned);
        Assert.AreEqual(2, _progress[0].Total);
    }
}
