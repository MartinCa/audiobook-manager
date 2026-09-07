using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.RateLimiting;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>
/// Resolves a <see cref="ConsistencyIssueType.MetadataRefreshFailed"/> issue by retrying the
/// refresh - "resolve" here means "look again", exactly like
/// <see cref="ConsistencyIssueType.UnreadableFile"/>: the source may have come back, the network
/// may have healed. There is nothing to repair on the book: a refresh never touches files or
/// tags, so no cleanup precedes the retry.
/// </summary>
public class MetadataRefreshFailedResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } =
        new[] { ConsistencyIssueType.MetadataRefreshFailed };

    private readonly IMetadataRefreshService _metadataRefreshService;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly ILogger<MetadataRefreshFailedResolver> _logger;

    public MetadataRefreshFailedResolver(
        IMetadataRefreshService metadataRefreshService,
        IConsistencyIssueRepository issueRepository,
        ILogger<MetadataRefreshFailedResolver> logger)
    {
        _metadataRefreshService = metadataRefreshService;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var audiobook = issue.Audiobook;

        MetadataRefreshResult result;

        try
        {
            result = await _metadataRefreshService.RefreshAudiobookAsync(audiobook.Id);
        }
        catch (HardcoverDailyLimitExceededException ex)
        {
            // The service only re-throws this to let the bulk refresh loop stop early; a single
            // resolve reaches it raw and would otherwise surface as a generic 500 from the
            // controller's catch-all - while the dedicated refresh endpoint returns the clear
            // message. Report it as NotResolved rather than a throw: LibraryConsistencyService's
            // bulk sweep only counts exceptions as failed, so a normal result here would count
            // every remaining limited issue as "succeeded" - and NotResolved (rather than
            // rethrowing) also keeps other sources' issues resolvable further down the batch.
            return (ResolveScope.NotResolved, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "daily_limit_reached",
                ex.Message));
        }

        if (result.Success)
        {
            // The refresh succeeded; the stored failure row is stale (and, if the fetch found
            // differences, a pending snapshot now exists as its own state). Delete only this
            // type: the book may carry unrelated issues this resolve never evaluated.
            await _issueRepository.DeleteByAudiobookIdAndTypesAsync(
                audiobook.Id, new[] { ConsistencyIssueType.MetadataRefreshFailed });

            _logger.LogInformation(
                "Metadata refresh for audiobook {AudiobookId} ('{Title}') succeeded on retry.",
                audiobook.Id, audiobook.BookName);

            return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
                issue.Id,
                issue.IssueType,
                "refresh_succeeded",
                "The metadata refresh succeeded on retry."));
        }

        // Still failing - the service has already replaced this book's issue row with the fresh
        // error, so the stored issue stays current. Nothing else was touched.
        return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "still_failing",
            result.Error ?? "The metadata refresh failed again."));
    }
}