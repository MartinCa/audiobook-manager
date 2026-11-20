namespace AudiobookManager.Services;

/// <summary>
/// A token that changes whenever a cover file does, for putting in a cover URL so the browser can
/// cache the image without revalidating: a cover is rewritten in place on save, so the URL alone
/// never says whether the picture is current, but the file's modified time does.
/// </summary>
public static class CoverVersion
{
    /// <summary>
    /// The cover file's last-write time in Unix milliseconds, or <c>null</c> when there is no
    /// cover or the file cannot be read (the caller then falls back to a revalidated URL).
    /// </summary>
    public static long? Of(string? coverFilePath)
    {
        if (string.IsNullOrEmpty(coverFilePath))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(coverFilePath);
            return file.Exists
                ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
