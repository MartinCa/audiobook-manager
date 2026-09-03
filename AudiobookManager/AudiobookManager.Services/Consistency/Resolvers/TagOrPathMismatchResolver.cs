using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// Handles both TagMismatch and WrongFilePath. Both are symptoms of the same thing - the file on
/// disk doesn't match the library metadata already in the database - so both are fixed the same
/// way: rewrite the m4b tags from the database and let the normal save pipeline relocate the file
/// and resync its sidecars if the path changes too. See the "Binding invariant" in CLAUDE.md -
/// UpdateAudiobook is the only place allowed to touch these fields.
///
/// This used to be two handlers. ResolveWrongFilePath re-parsed tags from the file itself
/// (assuming they were already correct) and only relocated it, then deleted every issue for the
/// book on success - including a TagMismatch it had never touched, so a coexisting tag mismatch
/// silently vanished from the list without ever being fixed. Going through the same
/// database-is-truth pipeline as TagMismatch always did closes that gap by construction: there's
/// no "assume tags are fine" path left to desync from what actually got resolved.
/// </summary>
public class TagOrPathMismatchResolver : IConsistencyIssueResolver
{
    public IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; } = new[]
    {
        ConsistencyIssueType.WrongFilePath, ConsistencyIssueType.TagMismatch
    };

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly IConsistencyIssueRepository _issueRepository;
    private readonly ILogger<TagOrPathMismatchResolver> _logger;

    public TagOrPathMismatchResolver(
        IAudiobookRepository audiobookRepository,
        IAudiobookService audiobookService,
        IConsistencyIssueRepository issueRepository,
        ILogger<TagOrPathMismatchResolver> logger)
    {
        _audiobookRepository = audiobookRepository;
        _audiobookService = audiobookService;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    public async Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue)
    {
        var dbAudiobook = await _audiobookRepository.GetByIdWithIncludesAsync(issue.AudiobookId);
        if (dbAudiobook == null)
            throw new KeyNotFoundException($"Audiobook {issue.AudiobookId} not found");

        var domain = AudiobookService.FromDb(dbAudiobook);

        var result = await RewriteTagsAndClearIssuesAsync(
            _audiobookService, _issueRepository, _logger,
            dbAudiobook, domain, issue.Id, issue.IssueType,
            logVerb: "Rewrote tags and aligned file path",
            resultMessage: "Tags and file path updated.");

        return (ResolveScope.AllForAudiobook, result);
    }

    /// <summary>
    /// Persists a rewritten domain audiobook through the same binding-invariant pipeline this
    /// resolver uses (<see cref="AudiobookService.UpdateAudiobook"/>, so m4b tags, library path,
    /// and sidecars all stay consistent), then clears every other stored issue for the book since
    /// the rewrite invalidates them all.
    ///
    /// Also called by <see cref="LibraryConsistencyService.ResolveTagMismatchSelectivelyAsync"/>,
    /// which computes a *different* domain object (the DB metadata merged with the user's chosen
    /// per-field overrides, rather than this resolver's unconditional full rewrite from DB
    /// metadata) but needs the identical persist/log/clear tail once it has one - shared here
    /// instead of duplicated so the two can't drift apart on what "resolved" actually does.
    /// </summary>
    public static async Task<ConsistencyResolveResult> RewriteTagsAndClearIssuesAsync(
        IAudiobookService audiobookService,
        IConsistencyIssueRepository issueRepository,
        ILogger logger,
        Audiobook dbAudiobook,
        DomainAudiobook domain,
        long issueId,
        ConsistencyIssueType issueType,
        string logVerb,
        string resultMessage)
    {
        await audiobookService.UpdateAudiobook(dbAudiobook.Id, domain);

        logger.LogInformation(
            "{Verb} for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            logVerb, dbAudiobook.Id, dbAudiobook.BookName, dbAudiobook.FileInfoFullPath);

        // Tags (and potentially the file path) changed, invalidating all other checks for this book
        await issueRepository.DeleteByAudiobookIdAsync(dbAudiobook.Id);

        return new ConsistencyResolveResult(issueId, issueType, "resolved", resultMessage);
    }
}
