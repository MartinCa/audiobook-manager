using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Services;

/// <summary>
/// Fixes a stored <c>SeriesPart</c> that does not match the position its matched series' roster
/// assigns the book. The rewrite goes through <see cref="AudiobookService.UpdateAudiobook"/>
/// exactly like <see cref="TagOrPathMismatchResolver"/>: the m4b tags, the recomputed library path
/// (and any relocation it implies), the sidecars and the database all update together, per the
/// no-DB-only-field-updates binding invariant. <see cref="ResolveScope.AllForAudiobook"/> is
/// therefore the right cascade too (the rewrite can relocate the file, so every other stored check
/// for the book is stale), and the whole resolve is covered by the per-audiobook save gate the
/// consistency service holds around every resolver except the person-scoped one.
///
/// The series detail's Part Mismatches section fixes the same books through the existing
/// expected-book-apply endpoint (<c>POST /api/series/expected-books/apply</c>), which is the
/// same <see cref="AudiobookService.UpdateAudiobook"/> pipeline with gate held controller-side;
/// the two are alternative entry points, not divergent implementations.
///
/// The issue's expected position was detected against the roster as it stood when the last full
/// check ran. A refresh since can renumber the series, so the resolve revalidates against the
/// current cached reconciliation (the same one the detector reports from): the position written
/// is the one the roster assigns *now*, never a value a refresh has already made obsolete. A book
/// the current roster no longer reports as a mismatch is a stale issue - the roster moved (or the
/// book changed) to make the stored part correct - and is cleared rather than "fixed" by writing
/// an obsolete part over a valid one. A book whose series field is empty is the same shape: it
/// cannot mismatch a series it no longer names, and the resolve says that cause explicitly instead
/// of implying the roster still agrees with the stored part (see
/// <see cref="ClearIssueBookNoLongerInSeries"/>).
/// </summary>
public class SeriesPartMismatchResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[] { ConsistencyIssueType.SeriesPartMismatch };

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly ISeriesReconciliationProvider _reconciliation;
    private readonly ILogger<SeriesPartMismatchResolver> _logger;

    public SeriesPartMismatchResolver(
        IAudiobookRepository audiobookRepository,
        IAudiobookService audiobookService,
        IConsistencyIssueRepository issueRepository,
        ISeriesReconciliationProvider reconciliation,
        ILogger<SeriesPartMismatchResolver> logger)
    {
        _audiobookRepository = audiobookRepository;
        _audiobookService = audiobookService;
        _issueRepository = issueRepository;
        _reconciliation = reconciliation;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var dbAudiobook = await _audiobookRepository.GetByIdWithIncludesAsync(issue.AudiobookId);
        if (dbAudiobook == null)
            throw new KeyNotFoundException($"Audiobook {issue.AudiobookId} not found");

        // The detector never emits an issue without an expected position; a stored one without it
        // is corrupt state, and silently clearing (or keeping) the part on that evidence would be
        // wrong - fail the resolve so the issue stays visible.
        if (string.IsNullOrWhiteSpace(issue.ExpectedValue))
        {
            throw new InvalidOperationException(
                $"SeriesPartMismatch issue {issue.Id} for audiobook {issue.AudiobookId} carries no expected position; refusing to guess.");
        }

        // The stored issue's expected part can be stale: it was captured against the roster as it
        // stood when the full check ran, and a refresh since may have renumbered the series. The
        // current position comes from the same cached per-series reconciliation the detector
        // reports from, so a written part can never be one the roster has already replaced. An
        // over-cap series throws (the detector skips it); let that failure surface here rather
        // than treating "cannot verify" as "stale" and clearing a possibly-live issue.
        if (string.IsNullOrWhiteSpace(dbAudiobook.Series))
        {
            return await ClearIssueBookNoLongerInSeries(issue);
        }

        var reconciliation = await _reconciliation.GetReconciliationAsync(dbAudiobook.Series);
        var currentMismatch = reconciliation.PartMismatches
            .FirstOrDefault(m => m.AudiobookId == dbAudiobook.Id);

        if (currentMismatch is null)
        {
            return await ClearStaleIssue(issue);
        }

        var domain = AudiobookService.FromDb(dbAudiobook);
        domain.SeriesPart = currentMismatch.ExpectedPart;

        await _audiobookService.UpdateAudiobook(dbAudiobook.Id, domain);

        _logger.LogInformation(
            "Set series part to '{Part}' for audiobook {AudiobookId} ('{Title}') at '{FilePath}' to match its matched series' roster",
            currentMismatch.ExpectedPart, dbAudiobook.Id, dbAudiobook.BookName, dbAudiobook.FileInfoFullPath);

        // The m4b tags and potentially the file path changed, invalidating all other checks for
        // this book - same cascade the tag/path rewrite applies.
        await _issueRepository.DeleteByAudiobookIdAsync(dbAudiobook.Id);

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Series part updated to the matched series' position and the book re-checked."));
    }

    /// <summary>
    /// The book's part agrees with what the current roster assigns its matched entry, so the issue
    /// was already resolved by the roster itself - most likely a refresh of the series between the
    /// full check and this resolve. Writing the stored expected part would be actively wrong, and
    /// the issue row is deleted rather than reported as a failure.
    /// <see cref="ResolveScope.IssueOnly"/>: nothing about the book was touched, so a bulk resolve
    /// must not treat the book's other issues as settled by this stale row.
    /// </summary>
    private async Task<(ResolveScope, ConsistencyResolveResult)> ClearStaleIssue(ConsistencyIssue issue)
    {
        _logger.LogInformation(
            "SeriesPartMismatch issue {IssueId} for audiobook {AudiobookId} is stale: the current roster assigns the book's stored part; clearing it.",
            issue.Id, issue.AudiobookId);

        await _issueRepository.DeleteAsync(issue.Id);

        return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "The book's stored part already matches what the series' roster now assigns; the stored issue was stale and has been cleared."));
    }

    /// <summary>
    /// The book carries no series at all (the field was cleared after the issue was detected), so
    /// there is no roster left for the stored part to disagree with. The issue is stale and
    /// cleared without touching the book, exactly as <see cref="ClearStaleIssue"/> does - but the
    /// message is factual about the cause rather than claiming the roster assigns the stored part.
    /// </summary>
    private async Task<(ResolveScope, ConsistencyResolveResult)> ClearIssueBookNoLongerInSeries(ConsistencyIssue issue)
    {
        _logger.LogInformation(
            "SeriesPartMismatch issue {IssueId} for audiobook {AudiobookId} is stale: the book no longer belongs to a series, so its stored part cannot mismatch one; clearing it.",
            issue.Id, issue.AudiobookId);

        await _issueRepository.DeleteAsync(issue.Id);

        return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "The book is no longer part of a series, so its stored part cannot mismatch one; the stored issue was stale and has been cleared."));
    }
}