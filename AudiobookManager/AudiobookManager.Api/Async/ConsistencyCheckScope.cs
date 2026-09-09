namespace AudiobookManager.Api.Async;

/// <summary>
/// Distinguishes which consistency run produced a <see cref="ConsistencyCheckProgress"/> /
/// <see cref="ConsistencyCheckComplete"/> event: the full-library check or a re-check of only
/// the explicitly selected books. Both runs share the same event types, so consumers that want
/// one or the other (the full-check page versus the selection bar) filter on this.
/// </summary>
public static class ConsistencyCheckScope
{
    public const string Library = "library";
    public const string Selected = "selected";
}