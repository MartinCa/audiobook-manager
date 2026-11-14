using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

public class PendingOnlineMatchService : IPendingOnlineMatchService
{
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPendingOnlineMatchRepository _repository;
    private readonly IScrapingService _scrapingService;
    private readonly IMetadataRefreshService _metadataRefreshService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<PendingOnlineMatchService> _logger;

    public PendingOnlineMatchService(
        IAudiobookRepository audiobookRepository,
        IPendingOnlineMatchRepository repository,
        IScrapingService scrapingService,
        IMetadataRefreshService metadataRefreshService,
        ISettingsService settingsService,
        ILogger<PendingOnlineMatchService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _repository = repository;
        _scrapingService = scrapingService;
        _metadataRefreshService = metadataRefreshService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<PendingOnlineMatchBatchResult> SearchSelectedAsync(
        IReadOnlyList<long> audiobookIds,
        IReadOnlyList<string> sourceNames,
        Func<int, int, int, int, Task> progressAction)
    {
        var books = await _audiobookRepository.GetByIdsWithIncludesAsync(audiobookIds);
        var booksById = books.ToDictionary(b => b.Id);
        var settings = await _settingsService.GetLibrarySettings();

        var (processed, succeeded, failed) = await BulkOperationRunner.RunAsync(
            audiobookIds,
            async id =>
            {
                if (!booksById.TryGetValue(id, out var book))
                {
                    throw new KeyNotFoundException($"Audiobook {id} not found");
                }

                // Same precedence BookEditForm seeds BookSearchDialog's query with (author(s) -
                // book name, falling back to book name, falling back to the on-disk file name) -
                // the two flows must not drift, since the whole point of the bulk search is "what
                // the interactive dialog would have searched for, run for many books at once". The
                // settings are read once for the whole batch: the book's main author (when it has one)
                // is the only author in the query, and each source gets the initials handling that
                // applies to it.
                var queries = MetadataSearchQueryBuilder.BuildPerSource(
                    sourceNames, book.Authors.Select(a => a.Name), book.BookName, book.FileInfoFileName,
                    settings, book.MainAuthorName);
                // Initials handling only re-spells author names, so emptiness is the same for every
                // source; the library-wide form stands in when no source was chosen at all.
                var libraryQuery = MetadataSearchQueryBuilder.Build(
                    book.Authors.Select(a => a.Name), book.BookName, book.FileInfoFileName,
                    settings.SearchInitialsHandling, book.MainAuthorName);
                if (string.IsNullOrWhiteSpace(libraryQuery))
                {
                    throw new InvalidOperationException($"Audiobook {id} has no title or file name to search with.");
                }

                // When every source would be asked the same thing (the usual case), it is one plain
                // search; only a real difference between sources needs the per-source form.
                var distinctQueries = queries.Values.Distinct(StringComparer.Ordinal).ToList();
                var multi = distinctQueries.Count <= 1
                    ? await _scrapingService.SearchMultiple(sourceNames, distinctQueries.FirstOrDefault() ?? libraryQuery)
                    : await _scrapingService.SearchMultiple(queries);
                var snapshots = multi.Results.Select(PendingRefreshPayload.FromSearchResult).ToList();

                await _repository.UpsertAsync(new PendingOnlineMatch
                {
                    AudiobookId = id,
                    SearchedAt = DateTime.UtcNow,
                    // A fresh search supersedes any previous rejection - the user just asked this
                    // book to be searched again, so it belongs back under Pending regardless of
                    // where it was before.
                    Status = PendingOnlineMatchStatus.Pending,
                    SourceNamesJson = System.Text.Json.JsonSerializer.Serialize(sourceNames),
                    ResultsJson = PendingOnlineMatchPayload.Serialize(snapshots),
                });
            },
            _logger,
            id => $"Failed to search online metadata for audiobook {id}",
            progressAction);

        return new PendingOnlineMatchBatchResult(processed, audiobookIds.Count, succeeded, failed);
    }

    public async Task ValidateSelectionAsync(long audiobookId, int resultIndex) =>
        await GetCandidateAsync(audiobookId, resultIndex);

    public async Task SelectResultAsync(long audiobookId, int resultIndex)
    {
        var candidate = await GetCandidateAsync(audiobookId, resultIndex);
        var fetched = await _scrapingService.GetBookDetails(candidate.Url);
        await _metadataRefreshService.ApplyFetchedResultAsSnapshotAsync(audiobookId, fetched);

        // Resolved either way: a diff-less fetch still means the search successfully confirmed
        // this candidate matches what the book already has, which is itself a useful outcome.
        await _repository.DeleteByAudiobookIdAsync(audiobookId);
    }

    private async Task<PendingRefreshPayload.Snapshot> GetCandidateAsync(long audiobookId, int resultIndex)
    {
        var row = await _repository.GetByAudiobookIdAsync(audiobookId);
        if (row is null)
        {
            throw new KeyNotFoundException($"Audiobook {audiobookId} has no pending online match.");
        }

        var results = PendingOnlineMatchPayload.Parse(row.ResultsJson);
        if (resultIndex < 0 || resultIndex >= results.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultIndex), resultIndex, $"Audiobook {audiobookId} has {results.Count} candidate result(s).");
        }

        return results[resultIndex];
    }

    public Task<bool> RejectAsync(long audiobookId) =>
        _repository.SetStatusAsync(audiobookId, PendingOnlineMatchStatus.Rejected);

    public Task<bool> DismissAsync(long audiobookId) =>
        _repository.DeleteByAudiobookIdAsync(audiobookId);

    public Task<(List<PendingOnlineMatch> Items, int Total)> GetPageAsync(
        PendingOnlineMatchStatus status, int page, int pageSize) =>
        _repository.GetPageWithAudiobookAsync(status, page * pageSize, pageSize);
}
