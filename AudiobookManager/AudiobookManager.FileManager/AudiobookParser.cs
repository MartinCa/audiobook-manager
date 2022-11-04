using ATL;
using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;

public static class AudiobookParser
{
    private static readonly List<string> _supportedExtensions = new List<string> { ".m4b" };

    public static bool IsSupported(FileInfo fileInfo)
    {
        return _supportedExtensions.Contains(fileInfo.Extension);
    }

    public static Audiobook ParseAudiobook(FileInfo fileInfo)
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
            SeriesPart = track.SeriesPart,
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

    public static async Task UpdateAudiobookTags(string filePath, Audiobook audiobook)
    {
        var track = new Track(filePath);

        if (!track.AudioFormat.Readable || track.AudioFormat.ID == -1)
        {
            throw new UnsupportedFormatException($"{filePath} not readable by ATL");
        }

        // Series
        string? group = null;
        string? albumSort = audiobook.BookName;
        string? title = $"{audiobook.Year} - {audiobook.BookName}";
        if (!string.IsNullOrEmpty(audiobook.Series))
        {
            var paddedSeriesPart = audiobook.SeriesPart is not null ? audiobook.SeriesPart.PadLeft(2, '0') : "";
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
        track.SeriesPart = audiobook.SeriesPart;
        track.WriteSpecialTag(SpecialTagField.Mp4SeriesPart, audiobook.SeriesPart);

        track.WriteSpecialTag(SpecialTagField.ItunesGapless, "1");
        track.WriteSpecialTag(SpecialTagField.ItunesMediaType, "2");

        track.EmbeddedPictures.Clear();
        if (audiobook.Cover is not null)
        {
            var picture = PictureInfo.fromBinaryData(Convert.FromBase64String(audiobook.Cover.Base64Data), PictureInfo.PIC_TYPE.Front);
            track.EmbeddedPictures.Add(picture);
        }

        await track.SaveAsync();
    }

    private static List<Person> ParsePersonsFromString(string str)
    {
        return str.Split(",").Where(x => !string.IsNullOrEmpty(x)).Select(x => new Person(x.Trim())).ToList();
    }

    private static string GetStringFromListOfPersons(IEnumerable<Person> persons)
    {
        return string.Join(", ", persons.Select(x => x.Name));
    }
}
