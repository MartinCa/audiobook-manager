namespace AudiobookManager.Services;

/// <summary>
/// How far a resolve reaches beyond the issue it was asked to fix. Resolving one issue routinely
/// deletes other stored issues for the same book, and a bulk resolve
/// (<see cref="LibraryConsistencyService"/>'s ResolveLoadedIssuesAsync) has to know which of its
/// remaining items that already took care of.
/// </summary>
public enum ResolveScope
{
    /// <summary>Only this issue row was removed.</summary>
    IssueOnly,

    /// <summary>
    /// Every stored issue for the book was removed - the path or the tags changed (or the book
    /// was deleted outright), which invalidates every other check for it.
    /// </summary>
    AllForAudiobook,

    /// <summary>
    /// The book's sidecar-content issues were all removed, because WriteMetadata rewrites
    /// desc.txt, reader.txt and metadata.opf together.
    /// </summary>
    SidecarsForAudiobook,

    /// <summary>
    /// Nothing was resolved and the issue row is unchanged - not a resolution failure the
    /// resolver could repair, but a condition that makes the attempt impossible right now
    /// (e.g. the source's daily request budget is spent). A bulk resolve must count these as
    /// failed rather than succeeded, and must keep going: the condition is per-source, so a
    /// later issue backed by a different source can still resolve.
    /// </summary>
    NotResolved,
}
