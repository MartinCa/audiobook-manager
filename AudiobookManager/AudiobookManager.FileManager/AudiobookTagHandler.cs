using System.Text.RegularExpressions;
using ATL;
using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.FileManager;

public class AudiobookTagHandler : IAudiobookTagHandler
{
    // Case-insensitive: a "Book.M4B" is the same supported format as "book.m4b", and treating
    // it as unsupported would hide it from both scans and - worse - from the orphan-directory
    // safety net that decides whether a folder can be deleted.
    private static readonly HashSet<string> _supportedExtensions =
        new(new[] { ".m4b" }, StringComparer.OrdinalIgnoreCase);
    private static readonly Regex _re_multple_part = new Regex(@"^(\d+\.?\d?)-(\d+\.?\d?)$", RegexOptions.Compiled);
    private static readonly Regex _re_float = new Regex(@"^(\d+)\.(\d+)$", RegexOptions.Compiled);

    private readonly ILogger _logger;
    private readonly IAtlLogging _atlLogging;

    public AudiobookTagHandler(ILogger<AudiobookTagHandler> logger, IAtlLogging atlLogging)
    {
        _logger = logger;
        _atlLogging = atlLogging;
    }

    public static bool IsSupported(FileInfo fileInfo)
    {
        return _supportedExtensions.Contains(fileInfo.Extension);
    }

    /// <summary>
    /// Reads an audiobook's tags. <paramref name="includeCoverData"/> controls whether the
    /// embedded cover's bytes are base64-encoded into the result: callers that only need to know
    /// whether a cover exists (the consistency check, the save round-trip verification) should
    /// pass false, because encoding a multi-megabyte picture allocates the bytes plus a string
    /// ~1.4x their size, once per book, for a value they never read.
    /// </summary>
    public Audiobook ParseAudiobook(FileInfo fileInfo, bool includeCoverData = true)
    {
        var track = new Track(fileInfo.FullName);

        if (!track.AudioFormat.Readable || track.AudioFormat.ID == -1)
        {
            throw new UnsupportedFormatException($"{fileInfo.FullName} not readable by ATL");
        }

        var authors = ParsePersonsFromString(track.AlbumArtist);
        var narrators = ParsePersonsFromString(track.Composer);

        var embeddedPicture = track.EmbeddedPictures.FirstOrDefault();
        AudiobookImage? cover = null;
        if (embeddedPicture is not null)
        {
            cover = includeCoverData
                ? new AudiobookImage(Convert.ToBase64String(embeddedPicture.PictureData), embeddedPicture.MimeType)
                : new AudiobookImage(string.Empty, embeddedPicture.MimeType);
        }

        return new Audiobook(authors, track.Album, track.Year, new AudiobookFileInfo(fileInfo))
        {
            Narrators = narrators,
            Subtitle = track.ReadSpecialTag(SpecialTagField.Subtitle),
            Series = track.GetSeries(),
            SeriesPart = track.GetSeriesPart(),
            Genres = ParseGenresFromString(track.Genre),
            Description = track.Description,
            Copyright = track.Copyright,
            Publisher = track.Publisher,
            Language = track.Language,
            Rating = track.ReadSpecialTag(SpecialTagField.Rating),
            Asin = track.ReadSpecialTag(SpecialTagField.ASIN),
            Www = track.ReadSpecialTag(SpecialTagField.Www),
            Cover = cover,
            DurationInSeconds = track.Duration
        };
    }

    /// <summary>
    /// ATL's <c>Track.Year</c> setter silently ignores any value below 1000 - nothing is written
    /// and the tag reads back as 0 - which made every book from antiquity (Tacitus, AD 98)
    /// permanently un-saveable via the round-trip check. Assigning <c>Track.Date</c> instead
    /// writes an ISO date (<c>0098-01-01</c>) that ATL reads back as year 98. Years of 1000 and
    /// above keep the plain <c>Year</c> path so their on-disk form (<c>2010</c>) is unchanged.
    /// Year 1 stays unwritable: <c>DateTime.MinValue</c> is what ATL treats as "no date".
    /// </summary>
    private static void WriteYear(Track track, int? year)
    {
        if (year is >= 2 and < 1000)
        {
            track.Date = new DateTime(year.Value, 1, 1);
            return;
        }

        track.Year = year;
    }

    public void SaveAudiobookTagsToFile(Audiobook audiobook, Action<float>? progressAction = null)
    {
        if (audiobook.FileInfo is null)
        {
            throw new ArgumentNullException(nameof(audiobook), "FileInfo is null");
        }

        _logger.LogInformation("({audiobookFile}) Saving tags to m4b file", audiobook.FileInfo.FullPath);

        var track = new Track(audiobook.FileInfo.FullPath);

        _logger.LogInformation("({audiobookFile}) Loaded track", audiobook.FileInfo.FullPath);

        if (!track.AudioFormat.Readable || track.AudioFormat.ID == -1)
        {
            throw new UnsupportedFormatException($"{audiobook.FileInfo.FullPath} not readable by ATL");
        }

        // Everything written to disk carries the qualifier suffixes; the domain object holds the
        // clean values (see BookQualifiers).
        var bookName = audiobook.EffectiveBookName;
        var series = audiobook.EffectiveSeries;

        // Series
        string? group = null;
        string? albumSort = bookName;
        string? title = $"{audiobook.Year} - {bookName}";
        if (!string.IsNullOrEmpty(series))
        {
            var paddedSeriesPart = audiobook.SeriesPart is not null ? PadSeriesPart(audiobook.SeriesPart) : "";
            var paddedSeriesPartWithLeadingSpace = audiobook.SeriesPart is not null ? $" {paddedSeriesPart}" : "";
            var groupSeriesPart = !string.IsNullOrEmpty(audiobook.SeriesPart) ? $", Book #{paddedSeriesPart}" : "";
            albumSort = $"{series}{paddedSeriesPartWithLeadingSpace} - {albumSort}";
            group = $"{series}{groupSeriesPart}";
            title = $"{series}{paddedSeriesPartWithLeadingSpace} - {title}";
        }

        track.AlbumArtist = GetStringFromListOfPersons(audiobook.Authors);
        track.Composer = GetStringFromListOfPersons(audiobook.Narrators);
        track.Album = bookName;
        track.WriteSpecialTag(SpecialTagField.Subtitle, audiobook.Subtitle);
        WriteYear(track, audiobook.Year);
        track.Artist = GetStringFromListOfPersons(audiobook.Authors.Concat(audiobook.Narrators));
        // ATL only clears a raw Track string property (as opposed to an AdditionalFields entry -
        // see WriteSpecialTag, which explicitly removes the key) when it's assigned an empty
        // string; assigning null is a silent no-op that leaves whatever value the file already
        // had on disk untouched. Every nullable field here has to be coerced to "" or "clearing"
        // it (e.g. blanking Description in the edit form on a file some other tool already
        // tagged) does nothing, and the round-trip verification below then fails with a
        // misleading "non-contiguous QuickTime chapters" hint that has nothing to do with the
        // real cause. WriteSpecialTag-backed fields (Subtitle, Rating, Asin, Www, Mp4Series, ...)
        // are unaffected - they already null-check and Remove().
        track.Group = group ?? "";
        track.Title = title;
        track.SortAlbum = albumSort;
        track.Genre = GetStringFromListOfGenres(audiobook.Genres);
        track.Description = audiobook.Description ?? "";
        track.Copyright = audiobook.Copyright ?? "";
        track.Publisher = audiobook.Publisher ?? "";
        track.Language = audiobook.Language ?? "";
        track.WriteSpecialTag(SpecialTagField.Rating, audiobook.Rating);
        track.WriteSpecialTag(SpecialTagField.ASIN, audiobook.Asin);
        track.WriteSpecialTag(SpecialTagField.Www, audiobook.Www);
        track.Comment = audiobook.Description ?? "";

        track.WriteSpecialTag(SpecialTagField.ShowMovement, !string.IsNullOrEmpty(series) ? "1" : "0");
        track.SeriesTitle = series ?? "";
        track.WriteSpecialTag(SpecialTagField.Mp4Series, series);
        track.WriteSeriesPart(audiobook.SeriesPart);

        track.WriteSpecialTag(SpecialTagField.ItunesGapless, "1");
        track.WriteSpecialTag(SpecialTagField.ItunesMediaType, "2");

        if (audiobook.Cover is not null)
        {
            track.EmbeddedPictures.Clear();
            var picture = PictureInfo.fromBinaryData(Convert.FromBase64String(audiobook.Cover.Base64Data), PictureInfo.PIC_TYPE.Front);
            track.EmbeddedPictures.Add(picture);
        }

        _logger.LogInformation("({audiobookFile}) prepared tags", audiobook.FileInfo.FullPath);

        float currentProgress = (float)0.1;

        progressAction?.Invoke(currentProgress);

        Action<float>? modifiedProgressAction = (float progress) =>
        {
            float modifiedProgress = ((1 - currentProgress) * progress) + currentProgress;
            progressAction?.Invoke(modifiedProgress);
        };

        var saveResult = track.Save(modifiedProgressAction);

        if (!saveResult)
        {
            throw new Exception("Tags could not be saved");
        }

        _logger.LogInformation("({audiobookFile}) Tags saved successfully to m4b file", audiobook.FileInfo.FullPath);
    }

    /// <summary>
    /// Splits a "/"-joined genre tag. An unset tag reads back as an empty string, and a naive
    /// Split would turn that into a single empty-named genre - which then gets persisted as a
    /// real Genre row that every genre-less book links to.
    /// </summary>
    public static List<string> ParseGenresFromString(string? genreTag) =>
        (genreTag ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>
    /// Joins genres into the "/"-delimited tag. "/" is the delimiter, so a genre that itself
    /// contains one (Goodreads' "FICTION/Classics") can only ever read back as separate genres;
    /// it is split here, and repeats dropped, so the tag holds exactly what a re-read will see.
    /// <see cref="TagConsistencyChecker.FormatGenres"/> must normalize the same way.
    /// </summary>
    public static string GetStringFromListOfGenres(IEnumerable<string> genres) =>
        string.Join("/", genres.SelectMany(g => ParseGenresFromString(g)).Distinct(StringComparer.Ordinal));

    public static List<Person> ParsePersonsFromString(string? str)
    {
        // Whitespace-only entries ("A, , B") must be dropped too, not just empty ones - otherwise
        // they become Person rows with a blank name.
        return (str ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => new Person(x))
            .ToList();
    }

    public static string GetStringFromListOfPersons(IEnumerable<Person> persons)
    {
        return string.Join(", ", persons.Select(x => x.Name).Distinct());
    }

    public static string? PadSeriesPart(string? seriesPart)
    {
        if (seriesPart is null)
        {
            return null;
        }

        var multiplePartMatch = _re_multple_part.Match(seriesPart);
        if (multiplePartMatch.Success)
        {
            return $"{PadNumber(multiplePartMatch.Groups[1].Value)}-{PadNumber(multiplePartMatch.Groups[2].Value)}";
        }

        return PadNumber(seriesPart);
    }

    private static string? PadNumber(string? num)
    {
        if (num is null)
        {
            return null;
        }

        var floatRegexMatch = _re_float.Match(num);
        if (floatRegexMatch.Success)
        {
            return $"{floatRegexMatch.Groups[1].Value.PadLeft(2, '0')}.{floatRegexMatch.Groups[2].Value}";
        }

        return num.PadLeft(2, '0');
    }
}
