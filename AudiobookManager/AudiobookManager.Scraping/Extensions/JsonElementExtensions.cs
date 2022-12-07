using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AudiobookManager.Scraping.Extensions;
public static class JsonElementExtensions
{
    public static JsonElement GetNestedProperty(this JsonElement jsonElement, params string[] properties)
    {
        var elem = jsonElement;
        foreach (var property in properties)
        {
            elem = elem.GetProperty(property);
        }

        return elem;
    }

    public static string? GetPropertyValueOrNull(this JsonElement jsonElement, string propertyName)
    {
        if (jsonElement.TryGetProperty(propertyName, out var property))
        {
            return property.GetString();
        }

        return null;
    }

    public static JsonElement? GetMatchingJsonElementOrNull(this List<JsonElement> jsonElements, Func<JsonElement, bool> predicate) {
        if(!jsonElements.Any(predicate))
        {
            return null;
        }

        return jsonElements.Single(predicate);
    }
}
