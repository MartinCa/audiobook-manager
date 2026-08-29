using System.Text.RegularExpressions;
using ATL;

namespace AudiobookManager.FileManager;

public static class TrackSpecialTagExtensions
{
    private static readonly Regex _mp4SeriesPartRegex = new Regex(@"^\d+$");

    public const string mp4Name = "MPEG-4 Part 14";

    private static readonly Dictionary<SpecialTagField, string> _emptyMap = new Dictionary<SpecialTagField, string>();

    // ATL strips the "----:mean:" prefix when reading back a freeform atom whose mean is the
    // default "com.apple.iTunes" namespace, exposing it in AdditionalFields under the bare field
    // name only (see ATL's MP4 reader). Since all these fields use that default namespace, the
    // keys here must be the bare names too, or writes round-trip to a key ReadSpecialTag never
    // finds (the value still lands in the same underlying atom on disk either way).
    private static readonly Dictionary<SpecialTagField, string> _mp4Map = new Dictionary<SpecialTagField, string>()
    {
        { SpecialTagField.ASIN, "ASIN" },
        { SpecialTagField.Rating, "RATING WMP" },
        { SpecialTagField.Subtitle, "SUBTITLE" },
        { SpecialTagField.Www, "WWWAUDIOFILE" },
        { SpecialTagField.ItunesGapless, "pgap" },
        { SpecialTagField.ItunesMediaType, "stik" },
        { SpecialTagField.ShowMovement, "shwm" },
        { SpecialTagField.Mp4Series, "SERIES" },
        { SpecialTagField.Mp4SeriesPart, "SERIES-PART" }
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

    public static void WriteSpecialTag(this Track track, SpecialTagField field, string? value)
    {
        var map = GetSpecialTagFieldMap(track);
        var fieldExists = map.TryGetValue(field, out var key);
        if (!fieldExists || key is null)
        {
            return;
        }

        // A previous save (or another tool) may have written this field code with different
        // casing. AdditionalFields matches field codes case-insensitively when saving, but keeps
        // whichever casing was already present, so remove any stale-cased entry first to avoid
        // the casing drifting away from our canonical key forever.
        var existingKey = track.AdditionalFields.Keys.FirstOrDefault(
            k => !k.Equals(key, StringComparison.Ordinal) && k.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existingKey is not null)
        {
            track.AdditionalFields.Remove(existingKey);
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
