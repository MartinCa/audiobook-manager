namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The user-chosen sort of the authors list. The wire names are the camelCase enum names
/// (<c>name</c>, <c>bookCount</c>, ...). A null sort means the list's own default order.
/// <see cref="MissingBooks"/>/<see cref="UpcomingBooks"/> depend on the fuzzy roster
/// reconciliation the repository cannot evaluate, so the caller supplies those counts.
/// </summary>
public enum AuthorSortKey
{
    Name,
    BookCount,
    LastRefreshed,
    MissingBooks,
    UpcomingBooks,
}

/// <summary>Sort of the series list; see <see cref="AuthorSortKey"/> for the conventions.</summary>
public enum SeriesSortKey
{
    Name,
    /// <summary>Owned books in the series.</summary>
    BookCount,
    LastRefreshed,
    MissingBooks,
    /// <summary>Owned / expected books, capped at 1; a series with no expected books has none and sorts last.</summary>
    Completion,
}

/// <summary>Sort of the book lists.</summary>
public enum BookSortKey
{
    Title,
    /// <summary>The release year - owned books only carry a year, not a full date.</summary>
    Year,
    Duration,
    LastRefreshed,
    /// <summary>Primary series name, then part number; books without a series come last.</summary>
    Series,
}

public record AuthorSort(AuthorSortKey Key, bool Descending);

public record SeriesSort(SeriesSortKey Key, bool Descending);

public record BookSort(BookSortKey Key, bool Descending);

/// <summary>Parses the <c>sortBy</c>/<c>sortDir</c> query parameters shared by the list endpoints.</summary>
public static class ListSortParser
{
    /// <summary>
    /// Returns false (with a message naming the valid values) for an unknown key or direction.
    /// A null/blank <paramref name="sortBy"/> means "no sort" and succeeds with a null result,
    /// whatever <paramref name="sortDir"/> says - a direction alone changes nothing.
    /// </summary>
    public static bool TryParse<TKey>(string? sortBy, string? sortDir, out (TKey Key, bool Descending)? sort, out string? error)
        where TKey : struct, Enum
    {
        sort = null;
        error = null;

        var descending = false;
        if (!string.IsNullOrWhiteSpace(sortDir))
        {
            var dir = sortDir.Trim();
            if (string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase))
            {
                descending = true;
            }
            else if (!string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase))
            {
                error = "sortDir must be 'asc' or 'desc'.";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return true;
        }

        var name = sortBy.Trim();
        // Matched against the declared names rather than through Enum.TryParse alone, which also
        // accepts numeric strings ("1") and comma lists ("name,bookCount") - only the names are
        // part of the contract.
        var matchedName = Enum.GetNames<TKey>().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        if (matchedName is null)
        {
            var valid = string.Join(", ", Enum.GetNames<TKey>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
            error = $"sortBy must be one of: {valid}.";
            return false;
        }

        sort = (Enum.Parse<TKey>(matchedName), descending);
        return true;
    }
}
