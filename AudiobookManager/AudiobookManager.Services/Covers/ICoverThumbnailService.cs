namespace AudiobookManager.Services;

/// <summary>A cached, downscaled copy of a book's cover, ready to be served as a file.</summary>
/// <param name="Path">Absolute path of the cached JPEG.</param>
/// <param name="ETag">Validator that changes whenever the source cover does.</param>
public sealed record CoverThumbnail(string Path, string MimeType, DateTimeOffset LastModified, string ETag);

public interface ICoverThumbnailService
{
    /// <summary>The longest-edge sizes a thumbnail may be requested at; bounds what the cache can hold.</summary>
    IReadOnlyList<int> AllowedSizes { get; }

    /// <summary>
    /// Returns the cached thumbnail of <paramref name="coverFilePath"/>, generating it on first need.
    /// A cover that was rewritten since (different modified time or length) yields a fresh
    /// thumbnail and removes the superseded one.
    /// </summary>
    /// <returns>
    /// <c>null</c> when no thumbnail could be produced (unreadable image, cache not writable) -
    /// the caller should serve the original cover instead, because a missing thumbnail is no
    /// reason to show no cover at all.
    /// </returns>
    Task<CoverThumbnail?> GetOrCreateAsync(long audiobookId, string coverFilePath, int size, CancellationToken cancellationToken);
}
