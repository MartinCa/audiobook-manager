using AudiobookManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
        Encode(width, height, new PngEncoder(), flat: true);

    private static string FlatJpeg(int width = 200, int height = 200) =>
        Encode(width, height, new JpegEncoder(), flat: true);

    /// <summary>Per-pixel noise: incompressible, which is how a large payload is produced.</summary>
    private static string NoisyPng(int width, int height) =>
        Encode(width, height, new PngEncoder(), flat: false);

    /// <summary>
    /// A smooth gradient: large as a PNG, but compresses the way a photographic cover does, so it
    /// is what the size cap can meaningfully be asserted against. Noise cannot reach any cap and
    /// no real cover looks like it.
    /// </summary>
    private static string GradientPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32(
                        (byte)(x * 255 / accessor.Width),
                        (byte)(y * 255 / accessor.Height),
                        (byte)((x + y) * 255 / (accessor.Width + accessor.Height)));
                }
            }
        });

        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return Convert.ToBase64String(output.ToArray());
    }

    private static string Encode(int width, int height, IImageEncoder encoder, bool flat)
    {
        using var image = new Image<Rgba32>(width, height);
        if (!flat)
        {
            var random = new Random(42);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32(
                            (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                    }
                }
            });
        }
        else
        {
            image.Mutate(context => context.BackgroundColor(Color.CornflowerBlue));
        }

        using var output = new MemoryStream();
        image.Save(output, encoder);
        return Convert.ToBase64String(output.ToArray());
    }

    private static Image Decode(string base64) => Image.Load(Convert.FromBase64String(base64));

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
    // metadata search returns for some covers - round-trips through ImageSharp as an Adobe-marked
    // JPEG whose invalid transform flag browsers render as neon-green stripes. The original bytes
    // are valid and render everywhere, so they must be kept rather than re-encoded, exactly like a
    // small PNG on the fast path.
    [TestMethod]
    public void Normalize_ACmykJpeg_IsKeptByteForByte()
    {
        // 40x40 Adobe APP14 CMYK JPEG captured from Audible's CDN (779 bytes). ImageSharp's
        // encoder cannot produce CMYK, so the fixture is a real captured file embedded here.
        var cmykJpeg =
            "/9j/7gAOQWRvYmUAZAAAAAAA/9sAQwADAgIDAgIDAwMDBAMDBAUIBQUEBAUKBwcGCAwKDAwLCgsLDQ4SEA0OEQ4LCx" +
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

        var result = _processor.Normalize(cmykJpeg, "image/jpeg");

        Assert.AreEqual("image/jpeg", result.MimeType);
        Assert.AreEqual(cmykJpeg, result.Base64Data, "A CMYK JPEG must not be re-encoded.");
    }

    [TestMethod]
    public void Normalize_ACmykJpegDeclaredAsSomethingElse_IsCorrectedFromTheBytes()
    {
        var cmykJpeg = CmykFixture;
        var result = _processor.Normalize(cmykJpeg, "image/png");

        Assert.AreEqual("image/jpeg", result.MimeType, "The bytes decide the type, not the claim.");
        Assert.AreEqual(cmykJpeg, result.Base64Data);
    }

    [TestMethod]
    public void Normalize_AnRgbJpeg_IsStillReencodedNotPassedThrough()
    {
        // Ensures the CMYK guard does not accidentally match ordinary 3-component JPEGs: those
        // must keep the normal re-encode (and with it the dimension and size caps).
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
        using var decoded = Decode(result.Base64Data);
        Assert.IsInstanceOfType<JpegFormat>(decoded.Metadata.DecodedImageFormat);
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

    // A PNG signature is eight bytes, and DetectFormat reads no further - so a truncated or
    // corrupt PNG passes format detection and only fails when something actually reads the header.
    // On the PNG fast path that was Image.Identify, which was the one byte-touching call in this
    // class without a guard: it threw out of Normalize as a 500 for a cover that should be a 400.
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
    // ImageSharp cannot generate CMYK, so every CMYK test shares the same captured fixture.
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
