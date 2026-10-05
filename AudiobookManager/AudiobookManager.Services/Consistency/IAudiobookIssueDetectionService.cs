using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

/// <summary>
/// Pure per-book detection: inspects one audiobook's file on disk against its library metadata
/// and returns the issues found, without touching the database. Shared by the full-library scan
/// (<see cref="LibraryConsistencyService.RunConsistencyCheck"/>), the single-book recheck, and
/// <see cref="MissingMediaFileResolver"/>, which re-runs it after a "missing" file reappears.
/// </summary>
public interface IAudiobookIssueDetectionService
{
    /// <param name="maxNarratorsInPath">The library's narrator-in-path setting, read once by the caller (a full check must not re-read it per book).</param>
    List<BookConsistencyIssue> DetectIssues(Audiobook audiobook, int maxNarratorsInPath);

    /// <summary>
    /// Single-book detection: reads the library's narrator-in-path setting, then runs
    /// <see cref="DetectIssues"/> off the calling thread (it parses the whole m4b).
    /// </summary>
    Task<List<BookConsistencyIssue>> DetectIssuesAsync(Audiobook audiobook);
}
