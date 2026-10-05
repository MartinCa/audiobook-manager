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
/// The consistency check against real tag and path detectors, for books whose files carry a
/// qualifier suffix. The database holds the clean name and series plus the qualifiers; the file
/// holds the suffixed names, so the check has to reshape what it reads - but only for a book that
/// actually stores the qualifier.
/// </summary>
[TestClass]
public class BookQualifierConsistencyTests
{
    private string _libraryPath = null!;
    private Mock<IAudiobookTagHandler> _tagHandler = null!;
    private AudiobookIssueDetectionService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _libraryPath = Path.Combine(Path.GetTempPath(), $"abm-qualifiers-{Guid.NewGuid():N}");
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

    /// <summary>What the file's tags say: the names exactly as written, nothing reshaped.</summary>
    private DomainAudiobook FileTags(string bookName, string? series, string fullPath) =>
        new(new List<DomainPerson> { new("Lee Child") }, bookName, 1997, new AudiobookFileInfo(fullPath, Path.GetFileName(fullPath), 1000))
        {
            Series = series,
            SeriesPart = "1",
        };

    /// <summary>Creates a book file where the library path generator would put a book with these names.</summary>
    private string PlaceFileAt(string bookName, string? series, params string[] qualifiers)
    {
        var domain = FileTags(bookName, series, "/import/book.m4b");
        domain.Qualifiers = qualifiers.ToList();
        var fullPath = AudiobookFileHandler.JoinLibraryPath(
            _libraryPath, AudiobookFileHandler.GenerateRelativeAudiobookPath(domain, false));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "not really an m4b");
        return fullPath;
    }

    private static DbAudiobook DbBook(string bookName, string? series, string fullPath, string qualifiers) =>
        new(1, bookName, null, series, "1", 1997,
            null, null, null, null, null, null, null, null, null,
            fullPath, Path.GetFileName(fullPath), 1000)
        {
            Authors = new List<DbPerson> { new(1, "Lee Child") },
            Qualifiers = qualifiers,
        };

    private void FileSays(DomainAudiobook tags) =>
        _tagHandler.Setup(t => t.ParseAudiobook(It.IsAny<FileInfo>(), It.IsAny<bool>())).Returns(tags);

    [TestMethod]
    public void DetectIssues_ABookWhoseFileCarriesItsStoredQualifierSuffixes_HasNoIssues()
    {
        var path = PlaceFileAt("Killing Floor", "Jack Reacher", "dramatized");
        FileSays(FileTags("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)", path));

        var issues = _service.DetectIssues(DbBook("Killing Floor", "Jack Reacher", path, ",dramatized,"), false);

        Assert.AreEqual(0, issues.Count, string.Join("; ", issues.Select(i => $"{i.IssueType}: {i.ExpectedValue} / {i.ActualValue}")));
    }

    [TestMethod]
    public void DetectIssues_SeveralQualifiers_HaveNoIssuesWhenTheFileHasThemInAlphabeticalOrder()
    {
        var path = PlaceFileAt("Killing Floor", "Jack Reacher", "abridged", "dramatized");
        FileSays(FileTags("Killing Floor (Abridged) (Dramatized)", "Jack Reacher (Abridged) (Dramatized)", path));

        var issues = _service.DetectIssues(DbBook("Killing Floor", "Jack Reacher", path, ",abridged,dramatized,"), false);

        Assert.AreEqual(0, issues.Count, string.Join("; ", issues.Select(i => i.IssueType)));
    }

    // The regression guard for existing libraries: a book stored (and filed) before qualifiers
    // existed, whose name simply ends in "(Dramatized)", must not start reporting mismatches.
    [TestMethod]
    public void DetectIssues_AnExistingBookWhoseNameEndsInAQualifierLabel_IsNotFlaggedAsAMismatch()
    {
        var path = PlaceFileAt("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)");
        FileSays(FileTags("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)", path));

        var issues = _service.DetectIssues(DbBook("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)", path, ""), false);

        Assert.AreEqual(0, issues.Count, string.Join("; ", issues.Select(i => $"{i.IssueType}: {i.ExpectedValue} / {i.ActualValue}")));
    }

    [TestMethod]
    public void DetectIssues_AFileMissingAStoredQualifierSuffix_ReportsATagMismatchIncludingQualifiers()
    {
        var path = PlaceFileAt("Killing Floor", "Jack Reacher", "dramatized");
        FileSays(FileTags("Killing Floor", "Jack Reacher", path));

        var issues = _service.DetectIssues(DbBook("Killing Floor", "Jack Reacher", path, ",dramatized,"), false);

        var mismatch = issues.Single(i => i.IssueType == BookConsistencyIssueType.TagMismatch);
        StringAssert.Contains(mismatch.Description, "Qualifiers");
    }

    [TestMethod]
    public void DetectIssues_ASeriesThatLostItsSuffix_IsReportedRatherThanReadAsConsistent()
    {
        var path = PlaceFileAt("Killing Floor", "Jack Reacher", "dramatized");
        FileSays(FileTags("Killing Floor (Dramatized)", "Jack Reacher", path));

        var issues = _service.DetectIssues(DbBook("Killing Floor", "Jack Reacher", path, ",dramatized,"), false);

        Assert.IsTrue(issues.Any(i => i.IssueType == BookConsistencyIssueType.TagMismatch), "the series tag is wrong on disk");
    }

    [TestMethod]
    public void DetectIssues_ABookWhosePathLacksTheQualifierSuffix_ReportsWrongFilePath()
    {
        var path = PlaceFileAt("Killing Floor", "Jack Reacher");
        FileSays(FileTags("Killing Floor (Dramatized)", "Jack Reacher (Dramatized)", path));

        var issues = _service.DetectIssues(DbBook("Killing Floor", "Jack Reacher", path, ",dramatized,"), false);

        Assert.IsTrue(issues.Any(i => i.IssueType == BookConsistencyIssueType.WrongFilePath));
        Assert.IsFalse(issues.Any(i => i.IssueType == BookConsistencyIssueType.TagMismatch), "the tags themselves are right");
    }
}
