using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Services;

/// <summary>
/// Resolves an <see cref="ConsistencyIssueType.InitialsSpacingMismatch"/> issue by renaming the
/// offending person value to its canonical spacing on every book that carries it. This is the
/// "binding invariant" path (AGENTS.md: Author/Series/SeriesPart/Year/BookName changes go through
/// <see cref="AudiobookService.UpdateAudiobook"/>) - the value is never rewritten record-only,
/// because the file on disk has to follow it too.
///
/// The issue is person-scoped (one row per distinct value, see
/// <see cref="InitialsSpacingIssueDetector"/>), so this resolver is deliberately *not* gated by the
/// calling issue's <see cref="ConsistencyIssue.AudiobookId"/> gate the way the per-book resolvers
/// are: it takes the per-audiobook gate itself, once per book in its own loop, exactly like
/// <see cref="SimilarValueService.AlignAuthorsAsync"/>. <see cref="LibraryConsistencyService"/>
/// special-cases this type to skip the outer gate.
///
/// Because the issue is person-scoped, the resolve never clears a book's issues wholesale: a
/// rewritten book's per-book rows (tags, path, sidecars) are genuinely invalidated, but another
/// person's <see cref="ConsistencyIssueType.InitialsSpacingMismatch"/> row that merely shares
/// the representative book must survive, and the reported scope is
/// <see cref="ResolveScope.IssueOnly"/> so a bulk resolve does not cascade it away.
/// </summary>
public class InitialsSpacingResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[]
    {
        ConsistencyIssueType.InitialsSpacingMismatch
    };

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly IAudiobookSaveGate _saveGate;
    private readonly ILogger<InitialsSpacingResolver> _logger;

    /// <summary>
    /// Issue types whose rows are genuinely invalidated when a rewritten book goes through
    /// <see cref="AudiobookService.UpdateAudiobook"/>: tags were rewritten, the file may have been
    /// relocated and the sidecars and cover re-extracted, so every per-book check is stale for it.
    ///
    /// Deliberately excludes <see cref="ConsistencyIssueType.InitialsSpacingMismatch"/>: that type
    /// is person-scoped, not book-scoped - one row per distinct non-compliant value, with
    /// <see cref="ConsistencyIssue.AudiobookId"/> naming a representative book the value appears on.
    /// Rewriting a *different* person's name on that book says nothing about another non-compliant
    /// value the same book happens to carry, so those rows must survive. Derived from the enum
    /// rather than hand-listed so a newly added issue type is automatically swept as stale here too.
    /// </summary>
    private static readonly ConsistencyIssueType[] InvalidatedByRewriteTypes =
        Enum.GetValues<ConsistencyIssueType>()
            .Where(t => t != ConsistencyIssueType.InitialsSpacingMismatch)
            .ToArray();

    public InitialsSpacingResolver(
        IAudiobookRepository audiobookRepository,
        IAudiobookService audiobookService,
        IConsistencyIssueRepository issueRepository,
        IAudiobookSaveGate saveGate,
        ILogger<InitialsSpacingResolver> logger)
    {
        _audiobookRepository = audiobookRepository;
        _audiobookService = audiobookService;
        _issueRepository = issueRepository;
        _saveGate = saveGate;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var currentName = issue.ActualValue;
        var canonicalName = issue.ExpectedValue;
        if (string.IsNullOrEmpty(currentName) || string.IsNullOrEmpty(canonicalName))
        {
            throw new InvalidOperationException(
                $"InitialsSpacingMismatch issue {issue.Id} is missing the actual/expected person value");
        }

        var books = await _audiobookRepository.GetBooksByPersonNamesAsync(new[] { currentName });
        if (books.Count == 0)
        {
            // The person value no longer exists on any book (another resolve already renamed it, or
            // the books were deleted). The issue is stale; clear it rather than reporting a failure.
            // IssueOnly, not AllForAudiobook: nothing about any book was touched, so a bulk resolve
            // must not treat the representative book's other issues as settled by this row.
            await _issueRepository.DeleteAsync(issue.Id);
            return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
                issue.Id, issue.IssueType, "resolved",
                $"No book carries '{currentName}' anymore; the issue was stale and has been cleared."));
        }

        // Per-book gate + UpdateAudiobook, mirroring SimilarValueService.AlignAuthorsAsync: a book
        // that is busy (an interactive save has the gate) fails its own item and the batch carries
        // on. The failed ids are collected so issue cleanup below does not delete the issues of a
        // book that was NOT rewritten - they must stay visible for the next check.
        var failedIds = new List<long>();
        var (processed, succeeded, failed) = await BulkOperationRunner.RunAsync(
            books,
            async dbBook =>
            {
                using var lease = _saveGate.Acquire(dbBook.Id);

                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;

                if (domain.Authors.Any(a => a.Name == currentName))
                {
                    domain.Authors = domain.Authors
                        .Select(a => a.Name == currentName ? new DomainPerson(canonicalName) : a)
                        .ToList();
                }

                if (domain.Narrators.Any(n => n.Name == currentName))
                {
                    domain.Narrators = domain.Narrators
                        .Select(n => n.Name == currentName ? new DomainPerson(canonicalName) : n)
                        .ToList();
                }

                try
                {
                    await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
                }
                catch (Exception)
                {
                    lock (failedIds)
                    {
                        failedIds.Add(dbBook.Id);
                    }
                    throw;
                }
            },
            _logger,
            dbBook => $"Failed to rename '{currentName}' to '{canonicalName}' on audiobook {dbBook.Id}",
            progressAction: null);

        // Every book the person appeared on was rewritten (or at least attempted). Its per-book
        // issue rows are all stale now - an UpdateAudiobook may have relocated files and rewritten
        // tags and sidecars - EXCEPT InitialsSpacingMismatch rows for other persons: those are
        // person-scoped, and this book may merely be the representative for a different
        // non-compliant value it also carries whose name was never touched here. A book whose
        // update FAILED keeps its issues: deleting them would hide an unresolved item until the
        // next full check.
        foreach (var book in books.Where(b => !failedIds.Contains(b.Id)))
        {
            await _issueRepository.DeleteByAudiobookIdAndTypesAsync(book.Id, InvalidatedByRewriteTypes);
        }

        if (failed > 0)
        {
            // Some books failed; the person still appears on them, so this issue itself must stay
            // for the next check to re-flag them. Return IssueOnly, not AllForAudiobook: another
            // person's InitialsSpacingMismatch row on a rewritten book is still live, and a bulk
            // resolve must not treat the representative book as settled by this partial fix.
            return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
                issue.Id, issue.IssueType, "resolved",
                $"Renamed '{currentName}' to '{canonicalName}' on {succeeded} of {processed} books "
                + $"{failed} failed and will be re-flagged by the next check."));
        }

        // The rename succeeded on every book, so the resolved person's own row is stale now.
        await _issueRepository.DeleteAsync(issue.Id);

        // IssueOnly for the same reason as above: this resolve removed only this person's row plus
        // each rewritten book's stale per-book rows - never another person's InitialsSpacingMismatch.
        return (ResolveScope.IssueOnly, new ConsistencyResolveResult(
            issue.Id, issue.IssueType, "resolved",
            $"Renamed '{currentName}' to '{canonicalName}' on all {succeeded} books."));
    }
}