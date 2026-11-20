using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace AudiobookManager.Services;

/// <summary>
/// Generates small JPEG copies of covers on first request and keeps them on disk.
///
/// A library list shows each cover at ~48px, but the original (up to 1500px, up to 2 MB, and PNGs
/// are kept as-is) is what used to be sent. On a slow connection a page of those starved every
/// other request. The cache lives beside the database (<c>DbLocation</c>'s directory, which is a
/// persistent volume in the normal deployment), never in the library: the library is shared with
/// Audiobookshelf and must hold only files it should see.
///
/// A cache file is named by book id, size and the source's modified time and length, so a cover
/// rewritten in place simply misses the cache; no invalidation hook has to exist on the save path.
/// </summary>
public class CoverThumbnailService : ICoverThumbnailService
{
    public const string CacheDirectoryName = "cover-thumbnails";
    private const string JpegMimeType = "image/jpeg";
    private const int JpegQuality = 80;
    private const int MaxConcurrentGenerations = 2;

    private static readonly int[] Sizes = [64, 128, 256];

    /// <summary>How often a generation restarts because the cover was replaced underneath it.</summary>
    private const int MaxStampAttempts = 3;

    private readonly string _cacheDirectory;
    private readonly ILogger<CoverThumbnailService> _logger;

    public CoverThumbnailService(IOptions<AudiobookManagerSettings> settings, ILogger<CoverThumbnailService> logger)
    {
        var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.Value.DbLocation)) ?? ".";
        _cacheDirectory = Path.Combine(dbDirectory, CacheDirectoryName);
        _logger = logger;
    }

    // A first load of a page asks for ~50 thumbnails at once; each decodes a full-size cover.
    // Capping concurrency bounds CPU and peak memory instead of letting the page's burst decide.
    // The service is a singleton, so this is process-wide; internal so a test can hold it.
    internal SemaphoreSlim GenerationGate { get; } = new(MaxConcurrentGenerations);

    public IReadOnlyList<int> AllowedSizes => Sizes;

    public async Task<CoverThumbnail?> GetOrCreateAsync(
        long audiobookId, string coverFilePath, int size, CancellationToken cancellationToken)
    {
        if (!Sizes.Contains(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Not one of the allowed thumbnail sizes.");
        }

        try
        {
            // The stamp names the cache entry and is what the caller's cache headers are checked
            // against, so it has to describe the bytes the thumbnail was decoded from. A save can
            // replace the cover while this request waits for the gate or while it decodes, so the
            // stamp is re-read after both, and the generation starts over if it moved.
            for (var attempt = 0; attempt < MaxStampAttempts; attempt++)
            {
                var before = StampOf(coverFilePath);
                if (before is null)
                {
                    return null;
                }

                var (lastModified, stamp) = before.Value;
                var prefix = $"{audiobookId}-{size}-";
                var fileName = $"{prefix}{stamp}.jpg";
                var cachePath = Path.Combine(_cacheDirectory, fileName);

                if (!File.Exists(cachePath))
                {
                    Directory.CreateDirectory(_cacheDirectory);

                    await GenerationGate.WaitAsync(cancellationToken);
                    try
                    {
                        if (StampOf(coverFilePath)?.Stamp != stamp)
                        {
                            continue; // replaced while queued
                        }

                        // Another request may have produced it while this one waited for the gate.
                        if (!File.Exists(cachePath))
                        {
                            var encoded = await Task.Run(() => Downscale(coverFilePath, size), cancellationToken);
                            if (encoded is null)
                            {
                                return null;
                            }

                            if (StampOf(coverFilePath)?.Stamp != stamp)
                            {
                                continue; // replaced while decoding: these bytes may be either image
                            }

                            // Written to a temp name and moved, so a concurrent reader never sees a
                            // half-written file.
                            var tempPath = Path.Combine(_cacheDirectory, $".{Guid.NewGuid():N}.tmp");
                            try
                            {
                                await File.WriteAllBytesAsync(tempPath, encoded, cancellationToken);
                                File.Move(tempPath, cachePath, overwrite: true);
                            }
                            finally
                            {
                                // A cancelled request or a failed write/move would otherwise leave
                                // the partial file behind (the superseded-file sweep only looks at
                                // .jpg). Nothing to delete after a successful move.
                                TryDelete(tempPath);
                            }

                            RemoveSuperseded(prefix, fileName);
                        }
                    }
                    finally
                    {
                        GenerationGate.Release();
                    }
                }

                return new CoverThumbnail(cachePath, JpegMimeType, lastModified, $"\"t{size}-{stamp}\"");
            }

            // The cover kept changing under every attempt: no stable thumbnail, so the caller
            // serves the original (revalidated) instead of publishing a guess.
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Includes a cache directory that is not writable. Logged without the exception text's
            // paths being relayed anywhere: the caller just falls back to the original cover.
            _logger.LogWarning(ex, "Could not produce a cover thumbnail for audiobook {AudiobookId}", audiobookId);
            return null;
        }
    }

    private static (DateTimeOffset Modified, string Stamp)? StampOf(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        var modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        return (modified, $"{modified.ToUnixTimeMilliseconds():x}-{file.Length:x}");
    }

    private byte[]? Downscale(string coverFilePath, int size)
    {
        using var codec = SKCodec.Create(coverFilePath);
        if (codec is null)
        {
            _logger.LogWarning("Cover {CoverFile} could not be read as an image; no thumbnail made.", Path.GetFileName(coverFilePath));
            return null;
        }

        // Unpremul for the same reason CoverImageProcessor does it: compositing onto white below
        // needs the real colour under a transparent pixel, not one already darkened toward black.
        using var bitmap = SKBitmap.Decode(codec, codec.Info.WithAlphaType(SKAlphaType.Unpremul));
        if (bitmap is null)
        {
            return null;
        }

        // Fit inside the size box, never upscale, keep the aspect ratio (covers are not all square).
        var scale = Math.Min(1.0, Math.Min((double)size / bitmap.Width, (double)size / bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var flattened = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(flattened))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint();
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        using var flatImage = SKImage.FromBitmap(flattened);
        using var data = flatImage.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort, and must never mask the failure that brought us here.
        }
    }

    private void RemoveSuperseded(string prefix, string keepFileName)
    {
        try
        {
            foreach (var stale in Directory.EnumerateFiles(_cacheDirectory, $"{prefix}*.jpg"))
            {
                var name = Path.GetFileName(stale);
                if (name.StartsWith(prefix, StringComparison.Ordinal) && name != keepFileName)
                {
                    File.Delete(stale);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving a superseded thumbnail behind costs disk, not correctness.
            _logger.LogDebug(ex, "Could not remove superseded cover thumbnails");
        }
    }
}
