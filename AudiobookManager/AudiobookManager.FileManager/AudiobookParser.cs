using ATL;
using AudiobookManager.Domain;
using AudiobookManager.FileManager.Mediatypes;

namespace AudiobookManager.FileManager
{
    public class AudiobookParser
    {
        public static Audiobook ParseAudiobook(string filePath)
        {
            var track = new Track(filePath);

            if (!track.AudioFormat.Readable || track.AudioFormat.ID == -1)
            {
                throw new UnsupportedFormatException($"{filePath} not readable by ATL");
            }

            var mediafile = AudiotypeFactory.GetMediafileFromTrack(track);

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
                Subtitle = ExtractFromAdditionalFields(track, mediafile.GetAdditionalFieldsKey(SpecialTagField.Subtitle)),
                Series = track.SeriesTitle,
                SeriesPart = track.SeriesPart,
                Genres = track.Genre.Split("/").ToList(),
                Description = track.Description,
                Copyright = track.Copyright,
                Publisher = track.Publisher,
                Rating = ExtractFromAdditionalFields(track, mediafile.GetAdditionalFieldsKey(SpecialTagField.Rating)),
                Asin = ExtractFromAdditionalFields(track, mediafile.GetAdditionalFieldsKey(SpecialTagField.ASIN)),
                Www = ExtractFromAdditionalFields(track, mediafile.GetAdditionalFieldsKey(SpecialTagField.Www)),
                Cover = cover
            };
        }

        public static void UpdateAudiobookTags(string filePath, Audiobook audiobook)
        {
            var track = new Track(filePath);

            if (!track.AudioFormat.Readable || track.AudioFormat.ID == -1)
            {
                throw new UnsupportedFormatException($"{filePath} not readable by ATL");
            }

            var mediafile = AudiotypeFactory.GetMediafileFromTrack(track);

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
}
