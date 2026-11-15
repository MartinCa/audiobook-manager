namespace AudiobookManager.Domain;

/// <summary>
/// What to do with the "which one is the main one" choice when a source lists several authors
/// (or series) AND names one of them as the main one. Only consulted when the source lists two or
/// more; with fewer there is nothing to choose.
/// </summary>
public enum SourceNamesMainRule
{
    /// <summary>Leave the book's current main author / primary series alone (the default).</summary>
    KeepCurrent = 0,

    /// <summary>Take the one the source names.</summary>
    UseSources = 1,

    /// <summary>Do not decide: an unattended run sends the book to review, a review shows the picker.</summary>
    AskMe = 2,
}

/// <summary>
/// What to do with the choice when the source lists several but does NOT say which is the main one.
/// </summary>
public enum SourceSilentRule
{
    /// <summary>Leave the book's current main author / primary series alone (the default).</summary>
    KeepCurrent = 0,

    /// <summary>
    /// Use the first one the source lists, but only when the book has none of its own among them:
    /// a main author or primary series the book already has is kept, never replaced by "first".
    /// </summary>
    FirstListed = 1,

    /// <summary>No main author (main author only - a book always has a primary series).</summary>
    None = 2,

    /// <summary>Do not decide: an unattended run sends the book to review, a review shows the picker.</summary>
    AskMe = 3,
}

/// <summary>
/// The pair of settings for one choice. Sources differ in whether they designate a main one at all,
/// so the rule is keyed by that, not by source name.
/// </summary>
public readonly record struct ChoiceRule(SourceNamesMainRule WhenSourceNamesMain, SourceSilentRule WhenSourceSilent);

/// <summary>The choices a rule can be set for.</summary>
public static class MetadataChoiceKinds
{
    public const string MainAuthor = "MainAuthor";
    public const string PrimarySeries = "PrimarySeries";
}
