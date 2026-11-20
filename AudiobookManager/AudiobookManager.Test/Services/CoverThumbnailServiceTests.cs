using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace AudiobookManager.Test.Services;

[TestClass]
public class CoverThumbnailServiceTests
{
    private string _dir = null!;
    private CoverThumbnailService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"thumbs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _service = new CoverThumbnailService(
            Options.Create(new AudiobookManagerSettings { DbLocation = Path.Combine(_dir, "db.sqlite") }),
            NullLogger<CoverThumbnailService>.Instance);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    private string WriteCover(string name, int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static (int Width, int Height) Dimensions(string path)
    {
        using var codec = SKCodec.Create(path);
        return (codec.Info.Width, codec.Info.Height);
    }

    private string CacheDir => Path.Combine(_dir, CoverThumbnailService.CacheDirectoryName);

    [TestMethod]
    public async Task FirstRequest_GeneratesAJpegThumbnailFittingTheSizeAndKeepingTheAspectRatio()
    {
        var cover = WriteCover("cover.png", 800, 400);

        var thumb = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);

        Assert.IsNotNull(thumb);
        Assert.AreEqual("image/jpeg", thumb.MimeType);
        using var codec = SKCodec.Create(thumb.Path);
        Assert.AreEqual(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.AreEqual((128, 64), Dimensions(thumb.Path));
        Assert.IsTrue(thumb.Path.StartsWith(CacheDir));
    }

    [TestMethod]
    public async Task ASourceSmallerThanTheSize_IsNotUpscaled()
    {
        var cover = WriteCover("small.png", 40, 30);

        var thumb = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);

        Assert.AreEqual((40, 30), Dimensions(thumb!.Path));
    }

    [TestMethod]
    public async Task SecondRequest_ReusesTheCachedFileInsteadOfRegenerating()
    {
        var cover = WriteCover("cover.png", 500, 500);
        var first = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);
        // Backdated so a regeneration (which would write "now") is distinguishable from a reuse.
        var backdated = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(first!.Path, backdated);

        var second = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);

        Assert.AreEqual(first.Path, second!.Path);
        Assert.AreEqual(first.ETag, second.ETag);
        Assert.AreEqual(backdated, File.GetLastWriteTimeUtc(second.Path));
        Assert.AreEqual(1, Directory.GetFiles(CacheDir).Length);
    }

    // Regression guard for the cache's one real risk: the cover is rewritten in place on every
    // save, so a thumbnail keyed on the book id alone would show the old picture forever.
    [TestMethod]
    public async Task ACoverRewrittenInPlace_YieldsAFreshThumbnailAndDropsTheStaleOne()
    {
        var cover = WriteCover("cover.png", 500, 500);
        var before = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);

        WriteCover("cover.png", 900, 300); // same path, different content and length
        File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddMinutes(5));
        var after = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);

        Assert.AreNotEqual(before!.Path, after!.Path);
        Assert.AreNotEqual(before.ETag, after.ETag);
        Assert.AreEqual((128, 43), Dimensions(after.Path));
        Assert.IsFalse(File.Exists(before.Path), "the superseded thumbnail should be removed");
    }

    [TestMethod]
    public async Task EachSizeAndEachBook_HasItsOwnCacheEntry_AndPruningNeverTouchesAnotherBook()
    {
        var cover = WriteCover("cover.png", 500, 500);

        var book1Small = await _service.GetOrCreateAsync(1, cover, 64, CancellationToken.None);
        var book1Large = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);
        var book11 = await _service.GetOrCreateAsync(11, cover, 64, CancellationToken.None);

        Assert.AreEqual((64, 64), Dimensions(book1Small!.Path));
        Assert.AreEqual((128, 128), Dimensions(book1Large!.Path));
        Assert.IsTrue(File.Exists(book1Small.Path));
        Assert.IsTrue(File.Exists(book11!.Path));
        Assert.AreEqual(3, Directory.GetFiles(CacheDir).Length);
    }

    [TestMethod]
    public async Task AnUnreadableImage_YieldsNullSoTheCallerCanServeTheOriginal()
    {
        var bogus = Path.Combine(_dir, "not-an-image.jpg");
        File.WriteAllText(bogus, "definitely not an image");

        var thumb = await _service.GetOrCreateAsync(1, bogus, 128, CancellationToken.None);

        Assert.IsNull(thumb);
    }

    [TestMethod]
    public async Task AMissingCoverFile_YieldsNull()
    {
        Assert.IsNull(await _service.GetOrCreateAsync(1, Path.Combine(_dir, "gone.jpg"), 128, CancellationToken.None));
    }

    [TestMethod]
    public async Task ConcurrentFirstRequests_ForTheSameCover_AllSucceedWithOneCacheFile()
    {
        var cover = WriteCover("cover.png", 600, 600);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None)));

        Assert.IsTrue(results.All(r => r is not null && r.Path == results[0]!.Path));
        Assert.AreEqual(1, Directory.GetFiles(CacheDir).Length);
    }

    [TestMethod]
    public async Task ASizeOutsideTheAllowedSet_Throws()
    {
        var cover = WriteCover("cover.png", 100, 100);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => _service.GetOrCreateAsync(1, cover, 999, CancellationToken.None));
    }

    // A failed write/move used to leave its temp file on the database volume, where the
    // superseded-thumbnail sweep (which only looks at .jpg) never reaches it.
    [TestMethod]
    public async Task AFailedMoveIntoTheCache_LeavesNoTempFileBehind()
    {
        var cover = WriteCover("cover.png", 500, 500);
        var source = new FileInfo(cover);
        var stamp = $"{new DateTimeOffset(source.LastWriteTimeUtc, TimeSpan.Zero).ToUnixTimeMilliseconds():x}-{source.Length:x}";
        // A directory squatting on the target name makes the move fail after the temp is written.
        Directory.CreateDirectory(Path.Combine(CacheDir, $"1-128-{stamp}.jpg"));

        var thumb = await _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);

        Assert.IsNull(thumb);
        Assert.AreEqual(0, Directory.GetFiles(CacheDir, "*.tmp").Length);
    }

    // A cold page load queues ~50 thumbnail requests behind the generation gate. A cover saved
    // while its request is queued must not be published under the stamp taken before the save:
    // the controller would then match the old cv against it and pin the new bytes for a year.
    [TestMethod]
    public async Task ACoverReplacedWhileTheRequestIsQueued_IsThumbnailedFromTheNewFileUnderTheNewStamp()
    {
        var cover = WriteCover("cover.png", 500, 500);
        var oldModified = new DateTimeOffset(new FileInfo(cover).LastWriteTimeUtc, TimeSpan.Zero);

        await _service.GenerationGate.WaitAsync();
        await _service.GenerationGate.WaitAsync();
        Task<CoverThumbnail?> request;
        try
        {
            // Runs synchronously up to the gate, so the stamp of the OLD file is already taken.
            request = _service.GetOrCreateAsync(1, cover, 128, CancellationToken.None);
            Assert.IsFalse(request.IsCompleted);

            WriteCover("cover.png", 900, 300); // the save: same path, new content and length
            File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddMinutes(5));
        }
        finally
        {
            _service.GenerationGate.Release(2);
        }

        var thumb = await request;

        Assert.IsNotNull(thumb);
        Assert.AreEqual((128, 43), Dimensions(thumb.Path), "decoded from the replacement, not the old image");
        Assert.AreNotEqual(oldModified, thumb.LastModified, "stamped with the replacement's modified time");
        Assert.AreEqual(
            new DateTimeOffset(new FileInfo(cover).LastWriteTimeUtc, TimeSpan.Zero), thumb.LastModified);
        Assert.AreEqual(1, Directory.GetFiles(CacheDir, "*.jpg").Length);
    }
}
