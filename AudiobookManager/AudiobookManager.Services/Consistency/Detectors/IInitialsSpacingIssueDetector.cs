using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// Detects person values that do not follow the library-wide initials-spacing setting. Unlike the
/// <see cref="IConsistencyIssueDetector"/>s - each of which checks one audiobook's on-disk state -
/// this is a library-wide sweep over the loaded audiobook graph, because the finding is about a
/// *person value* (an author or narrator that may appear on many books), not about one book's
/// file. One issue is emitted per distinct non-compliant person value, with
/// <see cref="ConsistencyIssue.AudiobookId"/> pointing at a representative book.
/// </summary>
public interface IInitialsSpacingIssueDetector
{
    IEnumerable<ConsistencyIssue> Detect(IReadOnlyList<DbAudiobook> audiobooks, Domain.InitialsSpacing spacing);
}