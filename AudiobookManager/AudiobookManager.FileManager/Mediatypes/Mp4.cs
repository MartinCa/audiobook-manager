namespace AudiobookManager.FileManager.Mediatypes
{
    public class Mp4 : IMediafile
    {
        private static Dictionary<SpecialTagField, string> SpecialTagFieldMap = new Dictionary<SpecialTagField, string> {
            { SpecialTagField.ASIN, "----:com.apple.iTunes:ASIN" },
            { SpecialTagField.Rating, "----:com.apple.iTunes:RATING WMP" },
            { SpecialTagField.Subtitle, "----:com.apple.iTunes:SUBTITLE" },
            { SpecialTagField.Www, "----:com.apple.iTunes:WWWAUDIOFILE" },
            { SpecialTagField.ItunesGapless, "pgap" },
            { SpecialTagField.ItunesMediaType, "stik" },
            { SpecialTagField.ShowMovement, "shwm" }
        };

        public string GetAdditionalFieldsKey(SpecialTagField specialTagField)
        {
            if (!SpecialTagFieldMap.ContainsKey(specialTagField))
            {
                throw new UnsupportedFormatException($"SpecialTagField {specialTagField.ToString()} not supported for Mp4");
            }

            return SpecialTagFieldMap[specialTagField];
        }
    }
}
