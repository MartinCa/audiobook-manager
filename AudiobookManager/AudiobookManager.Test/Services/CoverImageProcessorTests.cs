using AudiobookManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace AudiobookManager.Test.Services;

/// <summary>
/// Real images, generated here rather than checked in: the behaviour under test is decided by the
/// actual bytes - the format the header declares, whether a re-encode gets under the size cap -
/// and a fixture file would fix those choices rather than exercise them.
/// </summary>
[TestClass]
public class CoverImageProcessorTests
{
    private CoverImageProcessor _processor = null!;

    [TestInitialize]
    public void Setup() =>
        _processor = new CoverImageProcessor(NullLogger<CoverImageProcessor>.Instance);

    /// <summary>Flat colour: compresses to almost nothing, so size never confounds a format test.</summary>
    private static string FlatPng(int width = 200, int height = 200) =>
        Encode(width, height, SKEncodedImageFormat.Png, flat: true);

    private static string FlatJpeg(int width = 200, int height = 200) =>
        Encode(width, height, SKEncodedImageFormat.Jpeg, flat: true);

    /// <summary>Per-pixel noise: incompressible, which is how a large payload is produced.</summary>
    private static string NoisyPng(int width, int height) =>
        Encode(width, height, SKEncodedImageFormat.Png, flat: false);

    /// <summary>
    /// A smooth gradient: large as a PNG, but compresses the way a photographic cover does, so it
    /// is what the size cap can meaningfully be asserted against. Noise cannot reach any cap and
    /// no real cover looks like it.
    /// </summary>
    private static string GradientPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(
                    (byte)(x * 255 / width),
                    (byte)(y * 255 / height),
                    (byte)((x + y) * 255 / (width + height))));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    private static string Encode(int width, int height, SKEncodedImageFormat format, bool flat)
    {
        using var bitmap = new SKBitmap(width, height);
        if (flat)
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(100, 149, 237)); // cornflower blue
        }
        else
        {
            var random = new Random(42);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor(
                        (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)));
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return Convert.ToBase64String(data.ToArray());
    }

    private static SKBitmap Decode(string base64) => SKBitmap.Decode(Convert.FromBase64String(base64));

    private static SKEncodedImageFormat DetectFormat(string base64)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(Convert.FromBase64String(base64)));
        return codec.EncodedFormat;
    }

    // The decision from the review: cover art is often flat artwork with lettering, which PNG
    // encodes better than JPEG - re-encoding a small one would trade bytes for visible artefacts
    // on the title text.
    [TestMethod]
    public void Normalize_ASmallPng_IsKeptByteForByte()
    {
        var png = FlatPng();

        var result = _processor.Normalize(png, "image/png");

        Assert.AreEqual("image/png", result.MimeType);
        Assert.AreEqual(png, result.Base64Data, "A PNG within the cap must not be re-encoded at all.");
    }

    // Regression: a CMYK/UcrK JPEG (Adobe APP14, 4 components) - which is what Audible's
    // metadata search returns for some covers - decodes to correct RGB pixels, but ImageSharp's
    // encoder used to derive its output color type from that source metadata rather than the
    // pixels it was writing, so it recreated the Adobe APP14 marker on the re-encode. Browsers
    // honour that marker and apply a color transform that paints the cover neon-green. SkiaSharp's
    // JPEG encoder never round-trips source color-space metadata like that, so a CMYK source just
    // takes the normal re-encode path (resizing, size cap) like every other JPEG.
    [TestMethod]
    public void Normalize_ACmykJpeg_IsReencodedWithoutTheAdobeMarkerThatCausesTheColorCorruption()
    {
        var cmykJpeg = CmykFixture;

        var result = _processor.Normalize(cmykJpeg, "image/jpeg");

        Assert.AreEqual("image/jpeg", result.MimeType);
        Assert.AreNotEqual(cmykJpeg, result.Base64Data, "A CMYK JPEG must go through the normal re-encode, not be passed through.");

        var outputBytes = Convert.FromBase64String(result.Base64Data);
        using (var codec = SKCodec.Create(new SKMemoryStream(outputBytes)))
        {
            Assert.AreEqual(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        }

        var adobeMarker = System.Text.Encoding.ASCII.GetBytes("Adobe");
        Assert.IsTrue(outputBytes.AsSpan().IndexOf(adobeMarker) < 0,
            "The re-encode must not carry an Adobe APP14 marker - that marker with the source's transform flag is what browsers use to apply the corrupting color transform.");
    }

    [TestMethod]
    public void Normalize_ACmykJpegDeclaredAsSomethingElse_IsCorrectedFromTheBytes()
    {
        var cmykJpeg = CmykFixture;
        var result = _processor.Normalize(cmykJpeg, "image/png");

        Assert.AreEqual("image/jpeg", result.MimeType, "The bytes decide the type, not the claim.");
    }

    [TestMethod]
    public void Normalize_AnRgbJpeg_IsStillReencoded()
    {
        var jpeg = FlatJpeg(4000, 4000);

        var result = _processor.Normalize(jpeg, "image/jpeg");

        Assert.AreEqual("image/jpeg", result.MimeType);
        Assert.AreNotEqual(jpeg, result.Base64Data, "An RGB JPEG must still be re-encoded.");
        using var decoded = Decode(result.Base64Data);
        Assert.AreEqual(1500, decoded.Width, "It must still be resized to the dimension cap.");
    }

    [TestMethod]
    public void Normalize_AJpeg_IsReencodedAsJpeg()
    {
        var result = _processor.Normalize(FlatJpeg(), "image/jpeg");

        Assert.AreEqual("image/jpeg", result.MimeType);
        Assert.AreEqual(SKEncodedImageFormat.Jpeg, DetectFormat(result.Base64Data));
    }

    // A cover with transparency has nowhere to put it in a JPEG. Regression: the encoder's own
    // default is to premultiply alpha away, which turns a transparent pixel black regardless of
    // its underlying color - compositing onto white instead keeps a semi-transparent edge close to
    // the color it shows in a normal renderer.
    [TestMethod]
    public void Normalize_APngWithTransparency_IsFlattenedOntoWhiteRatherThanTurningBlack()
    {
        // Past the dimension cap despite being a flat colour: dimensions alone must force the
        // re-encode path, or this would take the small-PNG passthrough and never reach Flatten.
        const int size = 1600;
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(255, 0, 0, 0)); // fully transparent red
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = Convert.ToBase64String(data.ToArray());

        var result = _processor.Normalize(png, "image/png");

        Assert.AreEqual("image/jpeg", result.MimeType, "Past the dimension cap, this must take the re-encode path.");
        using var decoded = Decode(result.Base64Data);
        var pixel = decoded.GetPixel(decoded.Width / 2, decoded.Height / 2);
        Assert.AreEqual(new SKColor(255, 255, 255), pixel, "A fully transparent source pixel must flatten to white, not black.");
    }

    // An oversized PNG is exactly the 4 MB cover the review found being carried as a ~5.5 MB
    // base64 string through the request body and into a TEXT column.
    [TestMethod]
    public void Normalize_ALargePng_IsConvertedToJpegAndShrinks()
    {
        var png = GradientPng(2400, 2400);
        var originalBytes = Convert.FromBase64String(png).Length;

        var result = _processor.Normalize(png, "image/png");

        Assert.AreEqual("image/jpeg", result.MimeType);
        var newBytes = Convert.FromBase64String(result.Base64Data).Length;
        Assert.IsTrue(newBytes < originalBytes,
            $"Expected the cover to shrink, went from {originalBytes} to {newBytes} bytes.");
        Assert.IsTrue(newBytes <= 2 * 1024 * 1024,
            $"Expected the cover under the 2 MB cap, got {newBytes} bytes.");
    }

    // The deliberate non-failure: the cap is what the re-encode aims for, not a rule about what a
    // book may have. Refusing to save a book because its cover compressed badly would be worse
    // than storing a large cover.
    [TestMethod]
    public void Normalize_AnImageThatCannotReachTheCap_IsStillAcceptedRatherThanRefused()
    {
        var result = _processor.Normalize(NoisyPng(2000, 2000), "image/png");

        Assert.AreEqual("image/jpeg", result.MimeType);
        using var decoded = Decode(result.Base64Data);
        Assert.AreEqual(1500, decoded.Width, "It is still resized, even though it stays over the cap.");
    }

    [TestMethod]
    public void Normalize_AnImageBeyondTheDimensionCap_IsResizedKeepingItsAspectRatio()
    {
        var result = _processor.Normalize(GradientPng(3000, 1500), "image/png");

        using var decoded = Decode(result.Base64Data);
        Assert.AreEqual(1500, decoded.Width);
        Assert.AreEqual(750, decoded.Height, "A non-square cover must not be stretched square.");
    }

    [TestMethod]
    public void Normalize_ASmallPngPastTheDimensionCap_IsStillResized()
    {
        // Flat colour, so it is well under the size cap: dimensions alone must be enough to
        // trigger the re-encode, or a 6000px cover would pass through untouched.
        var result = _processor.Normalize(FlatPng(4000, 4000), "image/png");

        using var decoded = Decode(result.Base64Data);
        Assert.AreEqual(1500, decoded.Width);
        Assert.AreEqual("image/jpeg", result.MimeType);
    }

    // The declared MIME type was previously taken at face value and stored.
    [TestMethod]
    public void Normalize_AMisdeclaredMimeType_IsCorrectedFromTheBytes()
    {
        var result = _processor.Normalize(FlatPng(), "image/jpeg");

        Assert.AreEqual("image/png", result.MimeType, "The bytes decide the type, not the claim.");
    }

    [TestMethod]
    public void Normalize_AMissingMimeType_IsStillAccepted()
    {
        var result = _processor.Normalize(FlatPng(), null);

        Assert.AreEqual("image/png", result.MimeType);
    }

    // Previously this reached Convert.FromBase64String and then ATL, which wrote it into the
    // book's tags as a picture.
    [TestMethod]
    public void Normalize_SomethingThatIsNotAnImage_IsRefused()
    {
        var notAnImage = Convert.ToBase64String("<html><body>hello</body></html>"u8.ToArray());

        var ex = Assert.ThrowsExactly<InvalidCoverImageException>(
            () => _processor.Normalize(notAnImage, "image/jpeg"));

        StringAssert.Contains(ex.Message, "recognised image format");
    }

    // A PNG signature is eight bytes, and format detection reads no further - so a truncated or
    // corrupt PNG is recognised as PNG and only fails when something actually reads the header.
    [TestMethod]
    public void Normalize_APngWhoseHeaderIsCorrupt_IsRefusedRatherThanThrowing()
    {
        var signatureThenGarbage = new byte[]
        {
            0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05,
        };

        var ex = Assert.ThrowsExactly<InvalidCoverImageException>(
            () => _processor.Normalize(Convert.ToBase64String(signatureThenGarbage), "image/png"));

        StringAssert.Contains(ex.Message, "could not be read");
    }

    [TestMethod]
    public void Normalize_DataThatIsNotBase64_IsRefused()
    {
        Assert.ThrowsExactly<InvalidCoverImageException>(
            () => _processor.Normalize("this is not base64 !!!", "image/jpeg"));
    }

    [TestMethod]
    public void Normalize_AnEmptyCover_IsRefused()
    {
        Assert.ThrowsExactly<InvalidCoverImageException>(() => _processor.Normalize("", "image/jpeg"));
    }

    // Refused on the encoded length, before any of it is decoded - the point is not to allocate
    // an oversized payload to discover it is oversized.
    [TestMethod]
    public void Normalize_APayloadPastTheDecodeLimit_IsRefusedWithoutDecoding()
    {
        var oversized = new string('A', 30 * 1024 * 1024);

        var ex = Assert.ThrowsExactly<InvalidCoverImageException>(
            () => _processor.Normalize(oversized, "image/jpeg"));

        StringAssert.Contains(ex.Message, "larger than");
    }

    // Real Adobe APP14 CMYK JPEG (40x40, 779 bytes) as served by Audible's CDN. Kept in one place:
    // neither ImageSharp nor SkiaSharp can encode CMYK, so every CMYK test shares the same captured
    // fixture.
    private static string CmykFixture => "/9j/7gAOQWRvYmUAZAAAAAAA/9sAQwADAgIDAgIDAwMDBAMDBAUIBQUEBAUKBwcGCAwKDAwLCgsLDQ4SEA0OEQ4LCx" +
            "AWEBETFBUVFQwPFxgWFBgSFBUU/8AAFAgAKAAoBEMRAE0RAFkRAEsRAP/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgME" +
            "BQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJCh" +
            "YXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqi" +
            "o6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/aAA4EQwBNAFkASw" +
            "AAPwD9G/G//LSv0b/4Tf8A6afrX3RXpFfP/jf/AJaUf8Jv/wBNP1oor5/8b/8ALSj/AITf/pp+tFFfP/jf/lpR/wAJ" +
            "v/00/Wiivn/xv/y0o/4Tf/pp+tFFf0AeN/8AlpXz/wD8Jv8A9NP1oor5/wDG/wDy0o/4Tf8A6afrRRXz/wCN/wDlpR" +
            "/wm/8A00/Wiivn/wAb/wDLSj/hN/8App+tFFfP/jf/AJaUf8Jv/wBNP1oor+gDxv8A8tK+f/8AhN/+mn60UV8/+N/+" +
            "WlH/AAm//TT9aKK+f/G//LSj/hN/+mn60UV8/wDjf/lpR/wm/wD00/Wiivn/AMb/APLSj/hN/wDpp+tFFf0AeN/+Wl" +
            "fP/wDwm/8A00/Wiivn/wAb/wDLSj/hN/8App+tFFfP/jf/AJaUf8Jv/wBNP1oor5/8b/8ALSj/AITf/pp+tFFfP/jf" +
            "/lpR/wAJv/00/Wiiv6APG/8Ay0r5/wD+E3/6afrRRXz/AON/+WlH/Cb/APTT9aKK+f8Axv8A8tKP+E3/AOmn60UV8/" +
            "8Ajf8A5aUf8Jv/ANNP1oor5/8AG/8Ay0o/4Tf/AKafrRRX/9k=";
}
