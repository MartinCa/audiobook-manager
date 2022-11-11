using AngleSharp.Dom;

namespace AudiobookManager.Scraping.Extensions;
public static class ElementExtensions
{
    public static bool TryGetTextFromQuerySelector(this IElement elem, string query, out string text)
    {
        text = string.Empty;

        var elemText = elem.QuerySelector(query)?.Text().Trim();

        if (string.IsNullOrEmpty(elemText))
        {
            return false;
        }

        text = elemText;
        return true;
    }
}
