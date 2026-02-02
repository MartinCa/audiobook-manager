using System.Text.RegularExpressions;
using ATL;

namespace AudiobookManager.FileManager;

public static class TrackSpecialTagExtensions
{
    private static readonly Regex _mp4SeriesPartRegex = new Regex(@"^\d+");

    public const string mp4Name = "MPEG-4 Part 14";

    private static readonly Dictionary<SpecialTagField, string> _emptyMap = new Dictionary<SpecialTagField, string>();

    private static readonly Dictionary<SpecialTagField, string> _mp4Map = new Dictionary<SpecialTagField, string>()
    {
        { SpecialTagField.ASIN, "----:com.apple.iTunes:ASIN" },
        { SpecialTagField.Rating, "----:com.apple.iTunes:RATING WMP" },
        { SpecialTagField.Subtitle, "----:com.apple.iTunes:SUBTITLE" },
        { SpecialTagField.Www, "----:com.apple.iTunes:WWWAUDIOFILE" },
        { SpecialTagField.ItunesGapless, "pgap" },
        { SpecialTagField.ItunesMediaType, "stik" },
        { SpecialTagField.ShowMovement, "shwm" },
        { SpecialTagField.Mp4Series, "----:com.apple.iTunes:SERIES" },
        { SpecialTagField.Mp4SeriesPart, "----:com.apple.iTunes:SERIES-PART" }
    };

    public static string? ReadSpecialTag(this Track track, SpecialTagField field)
    {
        var map = GetSpecialTagFieldMap(track);
        var fieldExists = map.TryGetValue(field, out var key);
        if (!fieldExists || key is null)
        {
            return null;
        }

        return ExtractFromAdditionalFields(track, key);
    }

    // TODO log
    public static void WriteSpecialTag(this Track track, SpecialTagField field, string? value)
    {
        var map = GetSpecialTagFieldMap(track);
        var fieldExists = map.TryGetValue(field, out var key);
        if (!fieldExists || key is null)
        {
            return;
        }

        if (value is null)
        {
            track.AdditionalFields.Remove(key);
        }
        else
        {
            track.AdditionalFields[key] = value;
        }
    }

    public static void WriteSeriesPart(this Track track, string? seriesPart)
    {
        track.WriteSpecialTag(SpecialTagField.Mp4SeriesPart, seriesPart);

        if (seriesPart is not null && track.AudioFormat.Name == mp4Name)
        {
            var regexMatch = _mp4SeriesPartRegex.Match(seriesPart);
            if (regexMatch.Success)
            {
                // Actually stored in Movement Part
                track.SeriesPart = regexMatch.Captures.Single().Value;
            }
            else
            {
                track.SeriesPart = null;
            }
        }

        else
        {
            track.SeriesPart = seriesPart;
        }
    }

    public static string? GetSeries(this Track track)
    {
        if (track.AudioFormat.Name == mp4Name)
        {
            // Prefer the custom iTunes SERIES tag, but fall back to the standard
            // SeriesTitle field (©mvn) for files tagged by external tools like Audiobookshelf
            return track.ReadSpecialTag(SpecialTagField.Mp4Series)
                ?? GetNullStringIfEmpty(track.SeriesTitle);
        }

        return track.SeriesTitle;
    }

    public static string? GetSeriesPart(this Track track)
    {
        if (track.AudioFormat.Name == mp4Name)
        {
            // Prefer the custom iTunes SERIES-PART tag, but fall back to the standard
            // Movement Part field (©mvi) for files tagged by external tools like Audiobookshelf
            return track.ReadSpecialTag(SpecialTagField.Mp4SeriesPart)
                ?? GetNullStringIfEmpty(track.SeriesPart);
        }

        return track.SeriesPart;
    }

    private static string? ExtractFromAdditionalFields(Track track, string key)
    {
        return track.AdditionalFields.TryGetValue(key, out var value) ? GetNullStringIfEmpty(value) : null;
    }

    private static string? GetNullStringIfEmpty(string? str)
    {
        return str is null || string.IsNullOrWhiteSpace(str) ? null : str;
    }

    private static Dictionary<SpecialTagField, string> GetSpecialTagFieldMap(Track track)
    {
        return track.AudioFormat.Name switch
        {
            mp4Name => _mp4Map,
            _ => _emptyMap
        };
    }
}
