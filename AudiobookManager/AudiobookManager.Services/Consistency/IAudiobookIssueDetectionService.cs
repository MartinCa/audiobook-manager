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
    List<ConsistencyIssue> DetectIssues(Audiobook audiobook);
}
