using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.Logging;

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
        await _audiobookService.UpdateAudiobook(issue.AudiobookId, domain);

        _logger.LogInformation(
            "Rewrote tags and aligned file path for audiobook {AudiobookId} ('{Title}') at '{FilePath}'",
            issue.AudiobookId, dbAudiobook.BookName, dbAudiobook.FileInfoFullPath);

        // Tags (and potentially the file path) changed, invalidating all other checks for this book
        await _issueRepository.DeleteByAudiobookIdAsync(issue.AudiobookId);

        return (ResolveScope.AllForAudiobook, new ConsistencyResolveResult(
            issue.Id,
            issue.IssueType,
            "resolved",
            "Tags and file path updated."));
    }
}
