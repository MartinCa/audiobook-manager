namespace AudiobookManager.Database.Repositories;

/// <summary>
/// Normalizes the "last refreshed" filter bounds shared by the book, series and author filters.
/// The stamps are stored as UTC, and the bounds are exact instants: <c>RefreshedAfter</c> is an
/// inclusive lower bound and <c>RefreshedBefore</c> an exclusive upper bound, so the two form a
/// half-open range that never double-counts or drops a refresh at the boundary.
/// </summary>
public static class RefreshBounds
{
    /// <summary>
    /// ASP.NET binds an ISO timestamp with a zone to a <see cref="DateTimeKind.Local"/> value (the
    /// server's zone), so convert back; an unspecified-kind value (a bare date or local-less
    /// timestamp) is read as UTC, the same zone the stamps are in.
    /// </summary>
    public static DateTime ToUtc(DateTime value) => ToUtc(value, TimeZoneInfo.Local);

    /// <summary>
    /// <see cref="ToUtc(DateTime)"/> against an explicit "local" zone. Exists so the
    /// <see cref="DateTimeKind.Local"/> branch can be tested under a fixed non-UTC zone: on a UTC
    /// machine <c>ToLocalTime()</c> returns identical ticks, so a test through
    /// <see cref="TimeZoneInfo.Local"/> cannot tell a converted value from an unconverted one.
    /// </summary>
    public static DateTime ToUtc(DateTime value, TimeZoneInfo localZone) => value.Kind switch
    {
        DateTimeKind.Local => TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified), localZone),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
