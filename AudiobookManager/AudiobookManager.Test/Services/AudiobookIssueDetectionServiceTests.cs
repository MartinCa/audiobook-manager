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

    // #1311: an unmounted subtree - a dead per-author or per-share mount - makes the whole
    // directory disappear, which File.Exists cannot tell from a deleted book. The parent
    // directory is what distinguishes them (deleting a book leaves its directory behind), and
    // the finding must never be the one whose resolution deletes the library record.
    [TestMethod]
    public void DetectIssues_TheFileAndItsParentDirectoryAreAbsent_ReportsLibraryPathUnavailable()
    {
        var bookSubtree = Path.Combine(_libraryPath, "Dead Author");
        var issues = _service.DetectIssues(MakeAudiobook(Path.Combine(bookSubtree, "gone.m4b")));

        Assert.AreEqual(ConsistencyIssueType.LibraryPathUnavailable, issues.Single().IssueType);
    }

    // The gap a reviewer found: File.Exists is documented to return false "if the caller does not
    // have sufficient permissions to read the specified file... regardless of the existence of
    // path". So a library whose share permissions changed reported every book as
    // MissingMediaFile - and resolving that DELETES the library record, which is the only place
    // the curated metadata lives.
    //
    // Runs for real only as an unprivileged user, because root ignores the mode bits. CI runs as
    // a normal user, so it is exercised there; inconclusive rather than silently green locally.
    [TestMethod]
    public void DetectIssues_APermissionDeniedFile_IsUnreadableRatherThanMissing()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Assert.Inconclusive(
                "Needs Unix mode bits and an unprivileged process; root bypasses them and Windows does not have them.");
            return;
        }

        // The *containing directory* is what has to deny access, not the file. stat(2) needs
        // execute permission on the parents and no permission at all on the file itself, so a
        // mode-000 file is still perfectly stat-able and File.Exists answers "yes" for it - only
        // opening it fails, which the catch around ParseAudiobook already covers. A directory
        // whose traverse bit is gone is the case where File.Exists collapses to "missing", and it
        // is the realistic one: a share that comes back with the wrong ownership.
        var deniedDirectory = Path.Combine(_libraryPath, "denied");
        Directory.CreateDirectory(deniedDirectory);
        var bookInDeniedDirectory = Path.Combine(deniedDirectory, "book.m4b");
        File.WriteAllText(bookInDeniedDirectory, "present, but out of reach");

        File.SetUnixFileMode(deniedDirectory, UnixFileMode.None);

        try
        {
            Assert.IsFalse(
                File.Exists(bookInDeniedDirectory),
                "Precondition: File.Exists must be reporting this present file as absent - that is the bug under test.");

            var issues = _service.DetectIssues(MakeAudiobook(bookInDeniedDirectory));

            Assert.AreEqual(
                ConsistencyIssueType.UnreadableFile,
                issues.Single().IssueType,
                "A file that is present but unreachable must never be answered with the resolution that deletes the record.");
        }
        finally
        {
            File.SetUnixFileMode(
                deniedDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // Not a missing file either, and the same reasoning applies: this must not reach the
    // resolution that deletes the library record. Runs everywhere - no privileges involved.
    [TestMethod]
    public void DetectIssues_ADirectoryWhereTheMediaFileShouldBe_IsUnreadableRatherThanMissing()
    {
        var directoryPath = Path.Combine(_libraryPath, "not-a-file.m4b");
        Directory.CreateDirectory(directoryPath);

        var issues = _service.DetectIssues(MakeAudiobook(directoryPath));

        Assert.AreEqual(ConsistencyIssueType.UnreadableFile, issues.Single().IssueType);
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
