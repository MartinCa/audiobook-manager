using System.Text.RegularExpressions;

namespace AudiobookManager.Scraping.Extensions;
public static class RegexExtensions
{
    public static bool TryMatch(this Regex regex, string? str, out Match match)
    {
        match = Match.Empty;
        if (str is null)
        {
            return false;
        }

        match = regex.Match(str);

        return match.Success;
    }
}
