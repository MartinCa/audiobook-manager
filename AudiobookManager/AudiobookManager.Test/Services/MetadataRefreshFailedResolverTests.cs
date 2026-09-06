using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.RateLimiting;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataRefreshFailedResolverTests
{
    private readonly Mock<IMetadataRefreshService> _refreshService = new();
    private readonly Mock<IConsistencyIssueRepository> _issueRepository = new();
    private readonly Mock<ILogger<MetadataRefreshFailedResolver>> _logger = new();

    private MetadataRefreshFailedResolver CreateResolver() =>
        new(_refreshService.Object, _issueRepository.Object, _logger.Object);

    private static ConsistencyIssue Issue(long audiobookId = 42) => new()
    {
        Id = 7,
        AudiobookId = audiobookId,
        Audiobook = new Database.Models.Audiobook(
            audiobookId, "A Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/book.m4b", "book.m4b", 1000),
        IssueType = ConsistencyIssueType.MetadataRefreshFailed,
        Description = "Metadata refresh failed",
        DetectedAt = DateTime.UtcNow,
    };

    [TestMethod]
    public async Task ResolveAsync_DailyLimitExceeded_ReportsActionableResultWithoutTouchingTheIssue()
    {
        // Regression (PR #1380 review): RefreshAudiobookAsync re-throws the daily-limit
        // exception so the bulk loop can stop early. A single resolve reached it raw and the
        // controller's catch-all turned it into a generic 500; the resolver must surface the
        // clear message the dedicated refresh endpoint gives for the same condition.
        var issue = Issue();
        _refreshService
            .Setup(s => s.RefreshAudiobookAsync(42))
            .ThrowsAsync(new HardcoverDailyLimitExceededException(5000));

        var (scope, result) = await CreateResolver().ResolveAsync(issue);

        Assert.AreEqual(ResolveScope.IssueOnly, scope);
        Assert.AreEqual("daily_limit_reached", result.ActionTaken);
        StringAssert.Contains(result.Message, "5000");
        // The failure row stays: nothing was refreshed, so there is nothing to clean up.
        _issueRepository.Verify(
            r => r.DeleteByAudiobookIdAndTypesAsync(It.IsAny<long>(), It.IsAny<IReadOnlyCollection<ConsistencyIssueType>>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ResolveAsync_StillFailing_KeepsTheIssue()
    {
        var issue = Issue();
        _refreshService
            .Setup(s => s.RefreshAudiobookAsync(42))
            .ReturnsAsync(new MetadataRefreshResult { Success = false, Error = "fetch failed" });

        var (scope, result) = await CreateResolver().ResolveAsync(issue);

        Assert.AreEqual(ResolveScope.IssueOnly, scope);
        Assert.AreEqual("still_failing", result.ActionTaken);
        Assert.AreEqual("fetch failed", result.Message);
    }

    [TestMethod]
    public async Task ResolveAsync_Success_DeletesOnlyTheRefreshFailureIssue()
    {
        var issue = Issue();
        _refreshService
            .Setup(s => s.RefreshAudiobookAsync(42))
            .ReturnsAsync(new MetadataRefreshResult { Success = true });

        var (scope, result) = await CreateResolver().ResolveAsync(issue);

        Assert.AreEqual(ResolveScope.IssueOnly, scope);
        Assert.AreEqual("refresh_succeeded", result.ActionTaken);
        _issueRepository.Verify(
            r => r.DeleteByAudiobookIdAndTypesAsync(
                42, It.Is<IReadOnlyCollection<ConsistencyIssueType>>(
                    types => types.Count == 1 && types.Contains(ConsistencyIssueType.MetadataRefreshFailed))),
            Times.Once);
    }
}