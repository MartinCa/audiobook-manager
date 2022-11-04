using ATL;

namespace AudiobookManager.FileManager;

public static class TrackSpecialTabExtensions
{
    private static readonly Dictionary<SpecialTagField, string> _emptyMap = new Dictionary<SpecialTagField, string>();

    private static readonly Dictionary<SpecialTagField, string> _mp4Map = new()
    {
        { SpecialTagField.ASIN, "----:com.apple.iTunes:ASIN" },
        { SpecialTagField.Rating, "----:com.apple.iTunes:RATING WMP" },
        { SpecialTagField.Subtitle, "----:com.apple.iTunes:SUBTITLE" },
        { SpecialTagField.Www, "----:com.apple.iTunes:WWWAUDIOFILE" },
        { SpecialTagField.ItunesGapless, "pgap" },
        { SpecialTagField.ItunesMediaType, "stik" },
        { SpecialTagField.ShowMovement, "shwm" }
    };

    public static string? ReadSpecialTag(this Track track, SpecialTagField field)
    {
        var map = GetSpecialTagFieldMap(track);
        var fieldExists = map.TryGetValue(field, out var key);
        if (!fieldExists)
        {
            return null;
        }

        return ExtractFromAdditionalFields(track, key);
    }

    public static void WriteSpecialTag(this Track track, SpecialTagField field, string? value)
    {
        var map = GetSpecialTagFieldMap(track);
        var fieldExists = map.TryGetValue(field, out var key);
        if (!fieldExists)
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
            "MPEG-4 Part 14" => _mp4Map,
            _ => _emptyMap
        };
    }
}
