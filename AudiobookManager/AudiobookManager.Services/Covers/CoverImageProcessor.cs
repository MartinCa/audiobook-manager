using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

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
        var format = DetectFormat(bytes);

        // PNG is kept rather than converted when it is already small enough: cover art is often
        // flat-coloured artwork with text, which is exactly what PNG encodes better than JPEG, and
        // re-encoding it would trade size for visible artefacts on the title lettering.
        if (format is PngFormat && bytes.Length <= MaxStoredBytes && !ExceedsMaxDimension(bytes))
        {
            LogIfMimeWasWrong(declaredMimeType, PngFormat.Instance.DefaultMimeType);
            return new AudiobookImage(base64Data, PngFormat.Instance.DefaultMimeType);
        }

        var (reencoded, width, height) = ToJpeg(bytes);

        _logger.LogInformation(
            "Normalized cover: {OriginalFormat} {OriginalKb} KB -> JPEG {NewKb} KB at {Width}x{Height}",
            format.Name, bytes.Length / 1024, reencoded.Length / 1024, width, height);

        return new AudiobookImage(Convert.ToBase64String(reencoded), JpegFormat.Instance.DefaultMimeType);
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

    private static IImageFormat DetectFormat(byte[] bytes)
    {
        try
        {
            // Reads the container header only. This is what makes the declared MIME type
            // advisory: a client that says image/jpeg and sends something else is corrected, and
            // one that sends something that is not an image at all is refused here rather than
            // discovered when ATL writes it into a book's tags.
            return Image.DetectFormat(bytes);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidCoverImageException("The cover image is not in a recognised image format.");
        }
    }

    private static bool ExceedsMaxDimension(byte[] bytes)
    {
        try
        {
            // Identify reads the header only - no pixels are decoded to answer this.
            var info = Image.Identify(bytes);
            return info.Width > MaxDimension || info.Height > MaxDimension;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            // Guarded for the same reason LoadImage is, and reachable for the same reason:
            // DetectFormat reads the container signature only, so a PNG whose signature is intact
            // but whose IHDR is missing or corrupt gets this far. Unguarded it left Normalize as an
            // unhandled exception - a 500 - for a cover the class promises to refuse with a 400.
            throw new InvalidCoverImageException("The cover image could not be read.");
        }
    }

    private (byte[] Bytes, int Width, int Height) ToJpeg(byte[] bytes)
    {
        using var image = LoadImage(bytes);

        if (image.Width > MaxDimension || image.Height > MaxDimension)
        {
            // Max, not Stretch: covers are not always square, and a stretched one looks wrong in
            // every player that shows it.
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(MaxDimension, MaxDimension),
                Mode = ResizeMode.Max,
            }));
        }

        var encoded = Encode(image, JpegQuality);
        foreach (var quality in FallbackJpegQualities)
        {
            if (encoded.Length <= MaxStoredBytes)
            {
                break;
            }

            encoded = Encode(image, quality);
        }

        // Deliberately not an error if it is still over the cap after the last fallback. The cap
        // is what this tries to reach, not a rule about what a book may have: refusing to save a
        // book because its cover compressed badly would be a worse outcome than a large cover.
        return (encoded, image.Width, image.Height);
    }

    private static Image LoadImage(byte[] bytes)
    {
        try
        {
            return Image.Load(bytes);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            // Reachable even though DetectFormat succeeded: the header can name a format the
            // rest of the file does not deliver.
            throw new InvalidCoverImageException("The cover image could not be read.");
        }
    }

    private static byte[] Encode(Image image, int quality)
    {
        using var output = new MemoryStream();

        // ColorType must be set explicitly. Left null, the encoder derives it from the decoded
        // image's JpegMetadata.ColorType - metadata carried over from the source file - rather
        // than from the pixel data it is actually about to write. A source CMYK/YCCK (Adobe
        // APP14) JPEG decodes to correct RGB pixels but keeps that metadata, so an unset
        // ColorType makes the encoder emit those RGB pixels behind a re-created Adobe APP14
        // marker claiming CMYK/YCCK again - which is exactly the marker browsers use to apply a
        // color transform that turns the cover neon-green. Forcing YCbCr writes a plain encode
        // that matches the pixels regardless of what format they came from.
        image.Save(output, new JpegEncoder { Quality = quality, ColorType = JpegEncodingColor.YCbCrRatio420 });
        return output.ToArray();
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
