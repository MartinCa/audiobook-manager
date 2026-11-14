using AudiobookManager.Api.Async;
using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DbPerson = AudiobookManager.Database.Models.Person;
using DomainPerson = AudiobookManager.Domain.Person;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;

namespace AudiobookManager.Test.Services;

/// <summary>
/// A readable m4b whose tags would generate a folder or file name over the file system's limit.
/// The path generator refuses it with <see cref="PathSegmentTooLongException"/>; the consistency
/// check must answer that as a verdict about the metadata, not let it read as "the media could not
/// be read" (which would also throw away every other check's result for the book).
/// </summary>
[TestClass]
public class OverlongPathConsistencyTests
{
    private string _libraryPath = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private AudiobookIssueDetectionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _libraryPath = Path.Combine(Path.GetTempPath(), $"abm-overlong-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_libraryPath);

        _tagHandler = new Mock<IAudiobookTagHandler>();
        _service = new AudiobookIssueDetectionService(
            Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = _libraryPath }),
            Mock.Of<AudiobookManager.Services.ISettingsService>(s => s.GetLibrarySettings() == Task.FromResult(new AudiobookManager.Domain.LibrarySettings())),
            _tagHandler.Object,
            new IBookConsistencyIssueDetector[] { new TagMismatchDetector(), new PathMismatchDetector() },
            NullLogger<AudiobookIssueDetectionService>.Instance);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_libraryPath))
        {
            Directory.Delete(_libraryPath, recursive: true);
        }
    }

    // The file sits under a short name, so the probe reads it fine; only its tags are the problem.
    private string PlaceReadableFile()
    {
        var fullPath = Path.Combine(_libraryPath, "short.m4b");
        File.WriteAllText(fullPath, "not really an m4b");
        return fullPath;
    }

    private static DomainAudiobook FileTags(string bookName, int year, string fullPath) =>
        new(new List<DomainPerson> { new("Lee Child") }, bookName, year, new AudiobookFileInfo(fullPath, Path.GetFileName(fullPath), 1000));

    private static DbAudiobook DbBook(string bookName, string fullPath) =>
        new(1, bookName, null, null, null, 1997,
            null, null, null, null, null, null, null, null, null,
            fullPath, Path.GetFileName(fullPath), 1000)
        {
            Authors = new List<DbPerson> { new(1, "Lee Child") },
        };

    [TestMethod]
    public void DetectIssues_AReadableFileWhoseTagsMakeAnOverlongName_IsNotReportedAsUnreadable()
    {
        var path = PlaceReadableFile();
        var longTitle = new string('a', 300);
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(FileTags(longTitle, 1997, path));

        var issues = _service.DetectIssues(DbBook(longTitle, path), AudiobookManager.Domain.LibraryPathOptions.None);

        Assert.IsFalse(issues.Any(i => i.IssueType == BookConsistencyIssueType.UnreadableFile),
            string.Join("; ", issues.Select(i => $"{i.IssueType}: {i.Description}")));
    }

    [TestMethod]
    public void DetectIssues_AnOverlongName_IsAWrongFilePathThatSaysWhatToShorten()
    {
        var path = PlaceReadableFile();
        var longTitle = new string('a', 300);
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(FileTags(longTitle, 1997, path));

        var issue = _service.DetectIssues(DbBook(longTitle, path), AudiobookManager.Domain.LibraryPathOptions.None).Single();

        Assert.AreEqual(BookConsistencyIssueType.WrongFilePath, issue.IssueType);
        StringAssert.Contains(issue.Description, "bytes");
        Assert.IsNull(issue.ExpectedValue, "there is no path the book could be moved to");
        Assert.AreEqual(path, issue.ActualValue);
        Assert.IsFalse(issue.Description.Contains(_libraryPath), "the description must not carry the container path");
    }

    [TestMethod]
    public void DetectIssues_AnOverlongName_KeepsTheOtherChecksResultsForTheBook()
    {
        var path = PlaceReadableFile();
        var longTitle = new string('a', 300);
        // The file says 1998, the database 1997: an unrelated TagMismatch the check has to keep
        // reporting next to the path verdict instead of discarding it with an exception.
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(FileTags(longTitle, 1998, path));

        var types = _service.DetectIssues(DbBook(longTitle, path), AudiobookManager.Domain.LibraryPathOptions.None).Select(i => i.IssueType).ToList();

        CollectionAssert.AreEquivalent(
            new[] { BookConsistencyIssueType.TagMismatch, BookConsistencyIssueType.WrongFilePath }, types);
    }

    [TestMethod]
    public void SingleBookOperationFailures_RelayTheTooLongMessageRatherThanAFixedSentence()
    {
        var message = SingleBookOperationFailures.Describe(
            new PathSegmentTooLongException("A folder name is 300 bytes long."),
            NullLogger.Instance, 1, "applying the refresh");

        Assert.AreEqual("A folder name is 300 bytes long.", message);
    }
}
