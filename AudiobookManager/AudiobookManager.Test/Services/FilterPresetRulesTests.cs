using System.Reflection;
using System.Text.Json;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Test.Services;

[TestClass]
public class FilterPresetRulesTests
{
    private static IReadOnlyDictionary<string, JsonElement> Filters(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static string Normalize(string scope, string json) =>
        FilterPresetRules.NormalizeFilters(scope, Filters(json));

    private static ArgumentException Rejected(string scope, string json) =>
        Assert.ThrowsExactly<ArgumentException>(() => Normalize(scope, json));

    [TestMethod]
    public void NormalizeFilters_ValidBookFilters_AreStoredInAFixedKeyOrder()
    {
        var stored = Normalize(
            FilterPresetScopes.Books,
            """{"queueStates":["NotQueued"],"sources":["Unsupported"],"minDurationInSeconds":600}""");

        Assert.AreEqual(
            """{"minDurationInSeconds":600,"queueStates":["NotQueued"],"sources":["Unsupported"]}""", stored);
    }

    [TestMethod]
    public void NormalizeFilters_EmptyListsAreDropped_AndDuplicatesCollapsed()
    {
        var stored = Normalize(
            FilterPresetScopes.Books, """{"genres":[],"sources":["Audible"," Audible ","Hardcover"]}""");

        Assert.AreEqual("""{"sources":["Audible","Hardcover"]}""", stored);
    }

    [TestMethod]
    public void NormalizeFilters_NoActiveFilters_IsRefused_BecauseAPresetOfNothingIsNotAPreset()
    {
        StringAssert.Contains(Rejected(FilterPresetScopes.Books, "{}").Message, "no active filters");
        StringAssert.Contains(Rejected(FilterPresetScopes.Books, """{"genres":[]}""").Message, "no active filters");
    }

    [TestMethod]
    public void NormalizeFilters_AKeyTheListDoesNotHave_IsRefusedByName()
    {
        // 'followed' is a series/author filter; the book list has none.
        StringAssert.Contains(Rejected(FilterPresetScopes.Books, """{"followed":true}""").Message, "'followed'");
        // 'q' is the search text, which a preset deliberately does not hold.
        StringAssert.Contains(Rejected(FilterPresetScopes.Series, """{"q":"mist","followed":true}""").Message, "'q'");
    }

    [TestMethod]
    public void NormalizeFilters_WrongValueShapes_AreRefused()
    {
        Rejected(FilterPresetScopes.Books, """{"sources":"Audible"}""");
        Rejected(FilterPresetScopes.Books, """{"sources":[1]}""");
        Rejected(FilterPresetScopes.Books, """{"sources":["  "]}""");
        Rejected(FilterPresetScopes.Series, """{"followed":"yes"}""");
        Rejected(FilterPresetScopes.Series, """{"minOwnedBooks":-1}""");
        Rejected(FilterPresetScopes.Series, """{"minOwnedBooks":1.5}""");
        Rejected(FilterPresetScopes.Authors, """{"refreshedAfter":"not a date"}""");
        Rejected(FilterPresetScopes.Authors, """{"refreshedAfter":5}""");
    }

    [TestMethod]
    public void NormalizeFilters_ListsLongerThanTheBound_AreRefused()
    {
        var tooMany = JsonSerializer.Serialize(new
        {
            genres = Enumerable.Range(0, FilterPresetRules.MaxListItems + 1).Select(i => $"g{i}"),
        });
        Rejected(FilterPresetScopes.Books, tooMany);

        var tooLong = JsonSerializer.Serialize(new { genres = new[] { new string('x', FilterPresetRules.MaxListItemLength + 1) } });
        Rejected(FilterPresetScopes.Books, tooLong);
    }

    [TestMethod]
    public void NormalizeFilters_InstantsBecomeUtcIso()
    {
        var stored = Normalize(
            FilterPresetScopes.Authors,
            """{"refreshedAfter":"2026-06-01T12:00:00+02:00","refreshedBefore":"2026-07-01T00:00:00Z"}""");

        Assert.AreEqual(
            """{"refreshedAfter":"2026-06-01T10:00:00.000Z","refreshedBefore":"2026-07-01T00:00:00.000Z"}""", stored);
    }

    [TestMethod]
    public void NormalizeFilters_ARangeTheEndpointWouldRefuse_IsRefusedHere()
    {
        // GetSeries/GetAuthors answer 400 for min > max and after > before; a preset like that
        // could never be applied, so it must not be saveable.
        StringAssert.Contains(Rejected(FilterPresetScopes.Series, """{"minOwnedBooks":9,"maxOwnedBooks":2}""").Message, "minOwnedBooks");
        StringAssert.Contains(Rejected(FilterPresetScopes.Books, """{"minDurationInSeconds":9,"maxDurationInSeconds":2}""").Message, "minDurationInSeconds");
        StringAssert.Contains(Rejected(FilterPresetScopes.Authors, """{"minBookCount":9,"maxBookCount":2}""").Message, "minBookCount");
        StringAssert.Contains(
            Rejected(FilterPresetScopes.Authors, """{"refreshedAfter":"2026-07-01T00:00:00Z","refreshedBefore":"2026-06-01T00:00:00Z"}""").Message,
            "refreshedAfter");
        // Equal bounds are fine.
        Normalize(FilterPresetScopes.Series, """{"minOwnedBooks":2,"maxOwnedBooks":2}""");
    }

    [TestMethod]
    public void NormalizeFilters_OversizedFilters_AreRefused()
    {
        var items = Enumerable.Range(0, FilterPresetRules.MaxListItems).Select(i => new string((char)('a' + (i % 26)), FilterPresetRules.MaxListItemLength - 5) + i);
        var json = JsonSerializer.Serialize(new
        {
            genres = items, languages = items, qualifiers = items, sources = items, queueStates = items,
        });

        StringAssert.Contains(Rejected(FilterPresetScopes.Books, json).Message, "too large");
    }

    [TestMethod]
    public void NormalizeName_TrimsAndEnforcesLengthAndPrintableText()
    {
        Assert.AreEqual("Unsupported backlog", FilterPresetRules.NormalizeName("  Unsupported backlog  "));
        Assert.ThrowsExactly<ArgumentException>(() => FilterPresetRules.NormalizeName("   "));
        Assert.ThrowsExactly<ArgumentException>(() => FilterPresetRules.NormalizeName(null));
        Assert.ThrowsExactly<ArgumentException>(() => FilterPresetRules.NormalizeName(new string('n', FilterPresetRules.MaxNameLength + 1)));
        Assert.ThrowsExactly<ArgumentException>(() => FilterPresetRules.NormalizeName("bad\nname"));
        Assert.AreEqual(FilterPresetRules.MaxNameLength, FilterPresetRules.NormalizeName(new string('n', FilterPresetRules.MaxNameLength)).Length);
    }

    [TestMethod]
    public void NormalizeScope_AcceptsTheThreeListsIgnoringCase_AndRefusesAnythingElse()
    {
        Assert.AreEqual("books", FilterPresetRules.NormalizeScope(" Books "));
        Assert.AreEqual("series", FilterPresetRules.NormalizeScope("SERIES"));
        Assert.AreEqual("authors", FilterPresetRules.NormalizeScope("authors"));
        Assert.ThrowsExactly<ArgumentException>(() => FilterPresetRules.NormalizeScope("narrators"));
        Assert.ThrowsExactly<ArgumentException>(() => FilterPresetRules.NormalizeScope(null));
    }

    [TestMethod]
    public void ParseStored_CorruptJson_ReadsAsNoFilters()
    {
        Assert.AreEqual(0, FilterPresetRules.ParseStored("{not json").Count);
        Assert.AreEqual(1, FilterPresetRules.ParseStored("""{"followed":true}""").Count);
    }

    // ---- Guards against the vocabulary drifting from the endpoints it feeds -------------------

    /// <summary>The non-filter parameters of the list actions: paging, search text and the retired 'matched'.</summary>
    private static readonly string[] NonFilterParameters = ["limit", "offset", "q", "page", "pageSize", "search", "matched"];

    private static List<string> FilterParametersOf(Type controller, string action) =>
        controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters()
            .Select(p => p.Name!)
            .Where(name => !NonFilterParameters.Contains(name))
            .ToList();

    [TestMethod]
    public void Keys_Books_AreExactlyTheFilterParametersOfTheBookListEndpoint()
    {
        CollectionAssert.AreEquivalent(
            FilterParametersOf(typeof(BrowseController), nameof(BrowseController.GetAudiobooks)),
            FilterPresetRules.Keys[FilterPresetScopes.Books].Keys.ToList());
    }

    // The books scope is one vocabulary shared by every endpoint that lists owned books (a book
    // preset is applied from the library, search, series detail, author detail and Missing Tags),
    // so each of them must accept every key, or a preset saved on one would silently not
    // filter on another.
    [TestMethod]
    public void Keys_Books_AreAcceptedByEveryEndpointThatListsOwnedBooks()
    {
        var bookKeys = FilterPresetRules.Keys[FilterPresetScopes.Books].Keys.ToList();
        var endpoints = new (Type Controller, string Action)[]
        {
            (typeof(BrowseController), nameof(BrowseController.SearchAudiobooks)),
            (typeof(BrowseController), nameof(BrowseController.GetAuthorDetail)),
            (typeof(SeriesController), nameof(SeriesController.GetSeriesDetail)),
            (typeof(MissingTagsController), nameof(MissingTagsController.GetAudiobooksMissingTags)),
        };

        foreach (var (controller, action) in endpoints)
        {
            var accepted = FilterParametersOf(controller, action);
            var missing = bookKeys.Where(key => !accepted.Contains(key)).ToList();
            Assert.AreEqual(0, missing.Count, $"{controller.Name}.{action} does not accept: {string.Join(", ", missing)}");
        }
    }

    [TestMethod]
    public void Keys_Series_AreExactlyTheFilterParametersOfTheSeriesListEndpoint()
    {
        CollectionAssert.AreEquivalent(
            FilterParametersOf(typeof(SeriesController), nameof(SeriesController.GetSeries)),
            FilterPresetRules.Keys[FilterPresetScopes.Series].Keys.ToList());
    }

    [TestMethod]
    public void Keys_Authors_AreExactlyTheFilterParametersOfTheAuthorListEndpoint()
    {
        CollectionAssert.AreEquivalent(
            FilterParametersOf(typeof(BrowseController), nameof(BrowseController.GetAuthors)),
            FilterPresetRules.Keys[FilterPresetScopes.Authors].Keys.ToList());
    }

    [TestMethod]
    public void EveryScope_HasAVocabulary_AndEveryQueueStateIsAcceptedAsAListValue()
    {
        foreach (var scope in FilterPresetScopes.All)
        {
            Assert.IsTrue(FilterPresetRules.Keys.ContainsKey(scope), scope);
        }

        // The queue-state options each list serves are exactly what a preset may store for it.
        foreach (var option in QueueState.ForBooks.Concat(QueueState.ForSeries).Concat(QueueState.ForAuthors))
        {
            Normalize(FilterPresetScopes.Books, $$"""{"queueStates":["{{option.Value}}"]}""");
        }
    }

    [TestMethod]
    public void QueueStateOptions_HaveUniqueValuesPerList_AndEveryListOffersNotQueuedFirst()
    {
        foreach (var options in new[] { QueueState.ForBooks, QueueState.ForSeries, QueueState.ForAuthors })
        {
            Assert.AreEqual(options.Count, options.Select(o => o.Value).Distinct().Count());
            Assert.AreEqual(QueueState.NotQueued, options[0].Value);
        }
    }

    [TestMethod]
    public void Controller_RouteIsAFixedActionPath_NeverAFreeTextSegment()
    {
        // Presets are addressed by numeric id; the free-text name is only ever in the body.
        var routes = typeof(FilterPresetsController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
            .Select(a => a.Template)
            .ToList();

        Assert.IsFalse(routes.Any(t => t is not null && t.Contains("{name")), "a preset name must never be a route parameter");
    }
}
