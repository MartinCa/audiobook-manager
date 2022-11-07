using System.Text.RegularExpressions;
using ATL;
using AudiobookManager.Domain;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.FileManager;

public class AudiobookTagHandler : IAudiobookTagHandler
{
    private static readonly List<string> _supportedExtensions = new List<string> { ".m4b" };
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

    public Audiobook ParseAudiobook(FileInfo fileInfo)
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
            cover = new AudiobookImage(
                Convert.ToBase64String(embeddedPicture.PictureData),
                embeddedPicture.MimeType);
        }

        return new Audiobook(authors, track.Album, track.Year)
        {
            Narrators = narrators,
            Subtitle = track.ReadSpecialTag(SpecialTagField.Subtitle),
            Series = track.SeriesTitle,
            SeriesPart = track.GetSeriesPart(),
            Genres = track.Genre.Split("/").ToList(),
            Description = track.Description,
            Copyright = track.Copyright,
            Publisher = track.Publisher,
            Rating = track.ReadSpecialTag(SpecialTagField.Rating),
            Asin = track.ReadSpecialTag(SpecialTagField.ASIN),
            Www = track.ReadSpecialTag(SpecialTagField.Www),
            Cover = cover,
            DurationInSeconds = track.Duration,
            FileInfo = new AudiobookFileInfo(fileInfo)
        };
    }

    public void SaveAudiobookTagsToFile(Audiobook audiobook)
    {
        if (audiobook.FileInfo is null)
        {
            throw new ArgumentNullException(nameof(audiobook), "FileInfo is null");
        }

        var track = new Track(audiobook.FileInfo.FullPath);

        if (!track.AudioFormat.Readable || track.AudioFormat.ID == -1)
        {
            throw new UnsupportedFormatException($"{audiobook.FileInfo.FullPath} not readable by ATL");
        }

        // Series
        string? group = null;
        string? albumSort = audiobook.BookName;
        string? title = $"{audiobook.Year} - {audiobook.BookName}";
        if (!string.IsNullOrEmpty(audiobook.Series))
        {
            var paddedSeriesPart = audiobook.SeriesPart is not null ? PadSeriesPart(audiobook.SeriesPart) : "";
            var paddedSeriesPartWithLeadingSpace = audiobook.SeriesPart is not null ? $" {paddedSeriesPart}" : "";
            var groupSeriesPart = !string.IsNullOrEmpty(audiobook.SeriesPart) ? $", Book #{paddedSeriesPart}" : "";
            albumSort = $"{audiobook.Series}{paddedSeriesPartWithLeadingSpace} - {albumSort}";
            group = $"{audiobook.Series}{groupSeriesPart}";
            title = $"{audiobook.Series}{paddedSeriesPartWithLeadingSpace} - {title}";
        }

        track.AlbumArtist = GetStringFromListOfPersons(audiobook.Authors);
        track.Composer = GetStringFromListOfPersons(audiobook.Narrators);
        track.Album = audiobook.BookName;
        track.WriteSpecialTag(SpecialTagField.Subtitle, audiobook.Subtitle);
        track.Year = audiobook.Year;
        track.Artist = GetStringFromListOfPersons(audiobook.Authors.Concat(audiobook.Narrators));
        track.Group = group;
        track.Title = title;
        track.SortAlbum = albumSort;
        track.Genre = string.Join("/", audiobook.Genres);
        track.Description = audiobook.Description;
        track.Copyright = audiobook.Copyright;
        track.Publisher = audiobook.Publisher;
        track.WriteSpecialTag(SpecialTagField.Rating, audiobook.Rating);
        track.WriteSpecialTag(SpecialTagField.ASIN, audiobook.Asin);
        track.WriteSpecialTag(SpecialTagField.Www, audiobook.Www);
        track.Comment = audiobook.Description;

        track.WriteSpecialTag(SpecialTagField.ShowMovement, !string.IsNullOrEmpty(audiobook.Series) ? "1" : "0");
        track.SeriesTitle = audiobook.Series;
        track.WriteSpecialTag(SpecialTagField.Mp4Series, audiobook.Series);
        track.WriteSeriesPart(audiobook.SeriesPart);

        track.WriteSpecialTag(SpecialTagField.ItunesGapless, "1");
        track.WriteSpecialTag(SpecialTagField.ItunesMediaType, "2");

        track.EmbeddedPictures.Clear();
        if (audiobook.Cover is not null)
        {
            var picture = PictureInfo.fromBinaryData(Convert.FromBase64String(audiobook.Cover.Base64Data), PictureInfo.PIC_TYPE.Front);
            track.EmbeddedPictures.Add(picture);
        }

        var saveResult = track.Save();

        if (!saveResult)
        {
            throw new Exception("Tags could not be saved");
        }

        _logger.LogInformation("Audiobook tags saved to {filePath}", audiobook.FileInfo.FullPath);
    }

    public string GenerateRelativeAudiobookPath(Audiobook audiobook)
    {
        if (audiobook.FileInfo is null)
        {
            throw new ArgumentNullException(nameof(audiobook), "FileInfo is null");
        }

        var filename = $"{audiobook.Year} - {audiobook.BookName}";

        var subtitle = audiobook.Subtitle is not null ? "" : $" - {audiobook.Subtitle}";
        string? fileDirectory;
        if (!string.IsNullOrEmpty(audiobook.Series))
        {
            var seriesPart = !string.IsNullOrEmpty(audiobook.SeriesPart) ? $" {PadSeriesPart(audiobook.SeriesPart)}" : "";
            filename = $"{audiobook.Series}{seriesPart} - {filename}";
            var seriesDirectory = !string.IsNullOrEmpty(audiobook.SeriesPart) ? $"Book {seriesPart} - " : "";
            fileDirectory = $"{GetStringFromListOfPersons(audiobook.Authors)}/{audiobook.Series}/{seriesDirectory}{audiobook.Year} - {audiobook.BookName}{subtitle}";
        }
        else
        {
            fileDirectory = $"{GetStringFromListOfPersons(audiobook.Authors)}/{audiobook.Year} - {audiobook.BookName}{subtitle}";
        }

        return $"{fileDirectory}/{filename}{Path.GetExtension(audiobook.FileInfo.FullPath)}";
    }

    private static List<Person> ParsePersonsFromString(string str)
    {
        return str.Split(",").Where(x => !string.IsNullOrEmpty(x)).Select(x => new Person(x.Trim())).ToList();
    }

    private static string GetStringFromListOfPersons(IEnumerable<Person> persons)
    {
        return string.Join(", ", persons.Select(x => x.Name));
    }

    private static string? PadSeriesPart(string? seriesPart)
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
