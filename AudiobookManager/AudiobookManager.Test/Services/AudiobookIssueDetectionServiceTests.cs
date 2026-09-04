using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class AudiobookIssueDetectionServiceTests
{
    private string _libraryPath = null!;
    private string _filePath = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private AudiobookIssueDetectionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _libraryPath = Path.Combine(Path.GetTempPath(), $"abm-detect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_libraryPath);
        _filePath = Path.Combine(_libraryPath, "book.m4b");
        File.WriteAllText(_filePath, "not really an m4b");

        _tagHandler = new Mock<IAudiobookTagHandler>();
        _service = new AudiobookIssueDetectionService(
            Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = _libraryPath }),
            _tagHandler.Object,
            Array.Empty<IConsistencyIssueDetector>(),
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

    private DbAudiobook MakeAudiobook(string fullPath) =>
        new(1, "Test Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            fullPath, Path.GetFileName(fullPath), 1000)
        {
            Authors = new List<Person> { new(1, "Author") }
        };

    // The finding: the catch-all around detection returned an empty list, which the consistency
    // screen renders identically to a healthy book. A corrupt m4b reported as perfectly
    // consistent, and the only sign was a warning in the container log.
    [TestMethod]
    public void DetectIssues_TheFileCannotBeParsed_ReportsUnreadableFile()
    {
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Throws(new InvalidDataException("Not an MP4 container"));

        var issues = _service.DetectIssues(MakeAudiobook(_filePath));

        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(ConsistencyIssueType.UnreadableFile, issues[0].IssueType);
        Assert.IsTrue(
            string.Equals(_filePath, issues[0].ExpectedValue, StringComparison.Ordinal),
            $"Expected the issue to name '{_filePath}', got '{issues[0].ExpectedValue}'.");
        StringAssert.Contains(issues[0].ActualValue!, "Not an MP4 container");
    }

    [TestMethod]
    public void DetectIssues_ThePermissionIsDenied_ReportsUnreadableFileRatherThanNothing()
    {
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Throws(new UnauthorizedAccessException("Access to the path is denied"));

        var issues = _service.DetectIssues(MakeAudiobook(_filePath));

        Assert.AreEqual(ConsistencyIssueType.UnreadableFile, issues.Single().IssueType);
    }

    // Unreadable and missing are different states with different resolutions - one deletes the
    // library record, the other must never - so the absent file must not come back as unreadable.
    [TestMethod]
    public void DetectIssues_TheFileIsAbsent_IsStillMissingMediaFileNotUnreadable()
    {
        var issues = _service.DetectIssues(MakeAudiobook(Path.Combine(_libraryPath, "gone.m4b")));

        Assert.AreEqual(ConsistencyIssueType.MissingMediaFile, issues.Single().IssueType);
    }

    [TestMethod]
    public void DetectIssues_TheFileParses_ReportsWhatTheDetectorsFind()
    {
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>()))
            .Returns(new Domain.Audiobook(
                new List<Domain.Person>(), "Test Book", 2024,
                new Domain.AudiobookFileInfo(_filePath, "book.m4b", 1000)));

        var issues = _service.DetectIssues(MakeAudiobook(_filePath));

        Assert.AreEqual(0, issues.Count, "No detectors are registered in this fixture, so a readable file has no issues.");
    }
}
