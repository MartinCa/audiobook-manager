using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace AudiobookManager.Services;

/// <summary>
/// Validates and normalizes covers arriving from a client.
///
/// Every cover is carried as base64 in the JSON request body, written into the m4b's tags, saved
/// as cover.jpg beside the book, and - for an organize - serialized whole into the queue row's
/// json_audiobook TEXT column. A 4 MB JPEG becomes a ~5.5 MB base64 string doing all of that, and
/// nothing validated the bytes at all: the declared MIME type was taken at face value and the
/// decoded bytes went straight to the tag writer. The only bound was Kestrel's default 30 MB
/// request limit.
///
/// So: the format is decided by looking at the bytes rather than by what the client claims, and
/// anything that is not a readable image is refused rather than written into a book's tags.
/// </summary>
public class CoverImageProcessor : ICoverImageProcessor
{
    private const string PngMimeType = "image/png";
    private const string JpegMimeType = "image/jpeg";

    /// <summary>
    /// The largest encoded image this will decode at all. Well above any real cover; the point is
    /// that something has to bound the work before an unknown blob is handed to a decoder.
    /// </summary>
    private const int MaxDecodedBytes = 20 * 1024 * 1024;

    /// <summary>
    /// The size above which a cover is re-encoded rather than stored as-is.
    /// </summary>
    private const int MaxStoredBytes = 2 * 1024 * 1024;

    /// <summary>
    /// The longest edge a stored cover may have. Audible tops out around 2400px square; 1500 is
    /// past what any player displays and keeps a re-encoded cover comfortably under the size cap.
    /// </summary>
    private const int MaxDimension = 1500;

    private const int JpegQuality = 85;

    /// <summary>
    /// Tried in order when the first encode still comes out over the cap - a photographic cover at
    /// full dimensions occasionally does.
    /// </summary>
    private static readonly int[] FallbackJpegQualities = [70, 55];

    private readonly ILogger<CoverImageProcessor> _logger;

    public CoverImageProcessor(ILogger<CoverImageProcessor> logger)
    {
        _logger = logger;
    }

    public AudiobookImage Normalize(string base64Data, string? declaredMimeType)
    {
        var bytes = Decode(base64Data);
        using var codec = CreateCodec(bytes);

        // PNG is kept rather than converted when it is already small enough: cover art is often
        // flat-coloured artwork with text, which is exactly what PNG encodes better than JPEG, and
        // re-encoding it would trade size for visible artefacts on the title lettering.
        if (codec.EncodedFormat == SKEncodedImageFormat.Png
            && bytes.Length <= MaxStoredBytes
            && codec.Info.Width <= MaxDimension && codec.Info.Height <= MaxDimension)
        {
            LogIfMimeWasWrong(declaredMimeType, PngMimeType);
            return new AudiobookImage(base64Data, PngMimeType);
        }

        var (reencoded, width, height) = ToJpeg(codec);

        _logger.LogInformation(
            "Normalized cover: {OriginalFormat} {OriginalKb} KB -> JPEG {NewKb} KB at {Width}x{Height}",
            codec.EncodedFormat, bytes.Length / 1024, reencoded.Length / 1024, width, height);

        return new AudiobookImage(Convert.ToBase64String(reencoded), JpegMimeType);
    }

    private static byte[] Decode(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
        {
            throw new InvalidCoverImageException("The cover image is empty.");
        }

        // Checked before decoding, not after: base64 is 4 bytes per 3 encoded, so the length of
        // the string already bounds the result and there is no reason to allocate the payload of
        // an oversized request to find that out.
        var approximateDecodedSize = base64Data.Length / 4L * 3L;
        if (approximateDecodedSize > MaxDecodedBytes)
        {
            throw new InvalidCoverImageException(
                $"The cover image is larger than the {MaxDecodedBytes / (1024 * 1024)} MB this application will decode.");
        }

        try
        {
            return Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            throw new InvalidCoverImageException("The cover image is not valid base64.");
        }
    }

    private static SKCodec CreateCodec(byte[] bytes)
    {
        // Reads the container header only - no pixels are decoded to answer this. This is what
        // makes the declared MIME type advisory: a client that says image/jpeg and sends something
        // else is corrected, and one that sends something that is not an image at all is refused
        // here rather than discovered when ATL writes it into a book's tags.
        var codec = SKCodec.Create(new SKMemoryStream(bytes), out var result);
        if (codec != null && result == SKCodecResult.Success)
        {
            return codec;
        }

        codec?.Dispose();

        // Unimplemented/InvalidInput: the bytes do not match any container signature Skia
        // recognises at all. Anything else - IncompleteInput, ErrorInInput, ... - means a real
        // signature was recognised (e.g. a PNG's magic bytes) but the header or body past it could
        // not be parsed, which is a corrupt file rather than an unrecognised one.
        if (result is SKCodecResult.Unimplemented or SKCodecResult.InvalidInput)
        {
            throw new InvalidCoverImageException("The cover image is not in a recognised image format.");
        }

        throw new InvalidCoverImageException("The cover image could not be read.");
    }

    private (byte[] Bytes, int Width, int Height) ToJpeg(SKCodec codec)
    {
        var bitmap = DecodeBitmap(codec);
        try
        {
            if (bitmap.Width > MaxDimension || bitmap.Height > MaxDimension)
            {
                var resized = Resize(bitmap);
                bitmap.Dispose();
                bitmap = resized;
            }

            // JPEG has no alpha channel, so any transparency must be resolved before encoding.
            using var flattened = Flatten(bitmap);
            using var image = SKImage.FromBitmap(flattened);

            var encoded = Encode(image, JpegQuality);
            foreach (var quality in FallbackJpegQualities)
            {
                if (encoded.Length <= MaxStoredBytes)
                {
                    break;
                }

                encoded = Encode(image, quality);
            }

            // Deliberately not an error if it is still over the cap after the last fallback. The
            // cap is what this tries to reach, not a rule about what a book may have: refusing to
            // save a book because its cover compressed badly would be a worse outcome than a large
            // cover.
            return (encoded, flattened.Width, flattened.Height);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static SKBitmap DecodeBitmap(SKCodec codec)
    {
        // Decoded as Unpremul explicitly: SKBitmap.Decode(codec) alone defaults to premultiplied
        // output, which collapses a transparent or semi-transparent pixel's RGB toward black
        // before Flatten ever runs - discarding the source color under any pixel that has alpha,
        // even though the whole point of Flatten is to composite that color onto white.
        var info = codec.Info.WithAlphaType(SKAlphaType.Unpremul);
        var bitmap = SKBitmap.Decode(codec, info);
        if (bitmap == null)
        {
            // Reachable even though the codec was created successfully: the header can parse fine
            // while the pixel data past it does not decode.
            throw new InvalidCoverImageException("The cover image could not be read.");
        }

        return bitmap;
    }

    private static SKBitmap Resize(SKBitmap bitmap)
    {
        // Max, not Stretch: covers are not always square, and a stretched one looks wrong in every
        // player that shows it.
        var scale = Math.Min((double)MaxDimension / bitmap.Width, (double)MaxDimension / bitmap.Height);
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        var info = new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType);
        var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized == null)
        {
            throw new InvalidCoverImageException("The cover image could not be read.");
        }

        return resized;
    }

    private static SKBitmap Flatten(SKBitmap bitmap)
    {
        // Composited onto white rather than left to the encoder's own default, which is to
        // premultiply the source away and turn a transparent pixel black regardless of its
        // underlying color. Compositing onto white instead matches how tools like Photoshop
        // flatten to JPEG, and gives a semi-transparent edge the color it shows in a normal
        // renderer rather than a black fringe. Opaque source images pass through unchanged.
        var flattened = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(flattened);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bitmap, 0, 0);
        return flattened;
    }

    private static byte[] Encode(SKImage image, int quality)
    {
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private void LogIfMimeWasWrong(string? declaredMimeType, string actualMimeType)
    {
        if (!string.IsNullOrWhiteSpace(declaredMimeType)
            && !string.Equals(declaredMimeType, actualMimeType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Cover declared as {DeclaredMimeType} is actually {ActualMimeType}; using the detected type.",
                declaredMimeType, actualMimeType);
        }
    }
}
