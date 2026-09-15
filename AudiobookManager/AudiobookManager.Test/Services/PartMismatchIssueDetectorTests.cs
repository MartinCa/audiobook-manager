using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class PartMismatchIssueDetectorTests
{
    private Mock<ISeriesRepository> _seriesRepository = null!;
    private Mock<ISeriesService> _seriesService = null!;
    private PartMismatchIssueDetector _detector = null!;

    [TestInitialize]
    public void Setup()
    {
        _seriesRepository = new Mock<ISeriesRepository>();
        _seriesService = new Mock<ISeriesService>();
        _detector = new PartMismatchIssueDetector(
            _seriesRepository.Object, _seriesService.Object, NullLogger<PartMismatchIssueDetector>.Instance);
    }

    private static SeriesReconciliation MakeReconciliation(params SeriesPartMismatch[] mismatches) =>
        new(
            new List<SeriesExpectedBookInfo>(),
            new List<SeriesExpectedBookInfo>(),
            mismatches.ToList(),
            ExpectedBookCount: 0,
            OwnedCount: 0,
            Authors: new List<string>());

    private static SeriesPartMismatch MakeMismatch(long audiobookId, string expectedPart = "2", string? storedPart = "") =>
        new()
        {
            AudiobookId = audiobookId,
            BookName = $"Book {audiobookId}",
            StoredPart = storedPart,
            ExpectedPart = expectedPart,
            RosterTitle = $"Roster {expectedPart}",
        };

    // The sweep is one issue per treated book, drawn from the same cached reconciliation the
    // series detail renders - the detector must not reimplement any matching.
    [TestMethod]
    public async Task DetectLibraryWideAsync_MapsEveryMismatchAcrossMatchedSeriesToAnIssue()
    {
        _seriesRepository.Setup(r => r.GetMatchedSeriesNamesAsync()).ReturnsAsync(new List<string> { "Mistborn", "Stormlight" });
        _seriesService.Setup(s => s.GetReconciliationAsync("Mistborn"))
            .ReturnsAsync(MakeReconciliation(MakeMismatch(1, "2", ""), MakeMismatch(2, "3", "7")));
        _seriesService.Setup(s => s.GetReconciliationAsync("Stormlight"))
            .ReturnsAsync(MakeReconciliation(MakeMismatch(3, "1", null)));

        var issues = await _detector.DetectLibraryWideAsync();

        Assert.AreEqual(3, issues.Count);
        Assert.IsTrue(issues.All(i => i.IssueType == ConsistencyIssueType.SeriesPartMismatch));

        var byBook = issues.ToDictionary(i => i.AudiobookId);
        Assert.AreEqual("2", byBook[1].ExpectedValue);
        Assert.AreEqual("", byBook[1].ActualValue);
        StringAssert.Contains(byBook[1].Description, "Roster 2");
        Assert.AreEqual("3", byBook[2].ExpectedValue);
        Assert.AreEqual("7", byBook[2].ActualValue);
        Assert.IsNull(byBook[3].ActualValue, "a book with no part reports a null actual value");
    }

    // The bound of the sweep is the matched-series list - the only place a roster exists to
    // mismatch against. Unmatched series must never be reconciled.
    [TestMethod]
    public async Task DetectLibraryWideAsync_ReconcilesOnlyMatchedSeries()
    {
        _seriesRepository.Setup(r => r.GetMatchedSeriesNamesAsync()).ReturnsAsync(new List<string> { "Mistborn" });
        _seriesService.Setup(s => s.GetReconciliationAsync("Mistborn"))
            .ReturnsAsync(MakeReconciliation(MakeMismatch(1)));

        await _detector.DetectLibraryWideAsync();

        _seriesService.Verify(s => s.GetReconciliationAsync("Mistborn"), Times.Once);
        _seriesService.Verify(s => s.GetReconciliationAsync(It.IsAny<string>()), Times.Once,
            "no unmatched series may be reconciled by the sweep");
    }

    // Fail-soft: a series over the bounded-reconciliation caps throws from the reconciliation
    // (the series detail fails loudly on it); the sweep must skip it and keep going, not fail the
    // whole-consistency check.
    [TestMethod]
    public async Task DetectLibraryWideAsync_SeriesOverReconciliationCap_IsSkippedAndTheSweepContinues()
    {
        _seriesRepository.Setup(r => r.GetMatchedSeriesNamesAsync()).ReturnsAsync(new List<string> { "Broken", "Fine" });
        _seriesService.Setup(s => s.GetReconciliationAsync("Broken"))
            .ThrowsAsync(new InvalidOperationException("exceeds the cap"));
        _seriesService.Setup(s => s.GetReconciliationAsync("Fine"))
            .ReturnsAsync(MakeReconciliation(MakeMismatch(1)));

        var issues = await _detector.DetectLibraryWideAsync();

        Assert.AreEqual(1, issues.Count);
        Assert.AreEqual(1, issues[0].AudiobookId);
    }

    [TestMethod]
    public async Task DetectForAudiobookAsync_ReturnsOnlyTheGivenBooksMismatches()
    {
        _seriesService.Setup(s => s.GetReconciliationAsync("Mistborn"))
            .ReturnsAsync(MakeReconciliation(MakeMismatch(1, "2", ""), MakeMismatch(2, "3", "7")));

        var book = new DbAudiobook(
            1, "Book 1", null, "Mistborn", null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/Mistborn/Book 1.m4b", "Book 1.m4b", 1000);

        var issues = await _detector.DetectForAudiobookAsync(book);

        Assert.AreEqual(1, issues.Count);
        var issue = issues[0];
        Assert.AreEqual(1, issue.AudiobookId);
        Assert.AreEqual(ConsistencyIssueType.SeriesPartMismatch, issue.IssueType);
        Assert.AreEqual("2", issue.ExpectedValue);
    }

    [TestMethod]
    public async Task DetectForAudiobookAsync_BookWithNoSeries_ReturnsNothing()
    {
        var book = new DbAudiobook(
            1, "Book 1", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/Book 1.m4b", "Book 1.m4b", 1000);

        var issues = await _detector.DetectForAudiobookAsync(book);

        Assert.AreEqual(0, issues.Count);
        _seriesService.Verify(s => s.GetReconciliationAsync(It.IsAny<string>()), Times.Never,
            "a book outside any series cannot have a part mismatch");
    }

    [TestMethod]
    public async Task DetectForAudiobookAsync_SeriesOverReconciliationCap_FailsSoftWithNoIssues()
    {
        _seriesService.Setup(s => s.GetReconciliationAsync("Mistborn"))
            .ThrowsAsync(new InvalidOperationException("exceeds the cap"));

        var book = new DbAudiobook(
            1, "Book 1", null, "Mistborn", null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/Mistborn/Book 1.m4b", "Book 1.m4b", 1000);

        var issues = await _detector.DetectForAudiobookAsync(book);

        Assert.AreEqual(0, issues.Count);
    }
}