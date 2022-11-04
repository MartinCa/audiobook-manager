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

        if ( !track.AudioFormat.Readable || track.AudioFormat.ID == -1 )
        {
            throw new UnsupportedFormatException($"{fileInfo.FullName} not readable by ATL");
        }

        var authors = ParsePersonsFromString(track.AlbumArtist);
        var narrators = ParsePersonsFromString(track.Composer);

        var embeddedPicture = track.EmbeddedPictures.FirstOrDefault();
        AudiobookImage? cover = null;
        if ( embeddedPicture is not null )
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

    public static void UpdateAudiobookTags(string filePath, Audiobook audiobook)
    {
        var track = new Track(filePath);

        if ( !track.AudioFormat.Readable || track.AudioFormat.ID == -1 )
        {
            throw new UnsupportedFormatException($"{filePath} not readable by ATL");
        }


        track.AlbumArtist = GetStringFromListOfPersons(audiobook.Authors);

        // Special
        //track.Group = 
    }

    private static List<Person> ParsePersonsFromString(string str)
    {
        return str.Split(",").Where(x => !string.IsNullOrEmpty(x)).Select(x => new Person(x.Trim())).ToList();
    }

    private static string GetStringFromListOfPersons(List<Person> persons)
    {
        return string.Join(", ", persons.Select(x => x.Name));
    }

    private static string? ExtractFromAdditionalFields(Track track, string key)
    {
        return track.AdditionalFields.ContainsKey(key) ? NullableString(track.AdditionalFields[key]) : null;
    }

    private static string? NullableString(string? str)
    {
        return str is null || string.IsNullOrWhiteSpace(str) ? null : str;
    }
}
