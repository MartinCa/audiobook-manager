using AudiobookManager.Domain;

namespace AudiobookManager.Test.DomainModels;

/// <summary>
/// Per-source overrides of the search initials handling: a source with an entry uses it, every
/// other source follows the library-wide setting, and whatever is stored never throws on read.
/// </summary>
[TestClass]
public class MetadataSourceSearchSettingsTests
{
    private static Dictionary<string, SearchInitialsHandling> Overrides(params (string, SearchInitialsHandling)[] entries) =>
        MetadataSourceSearchSettings.Normalize(entries.Select(e => KeyValuePair.Create(e.Item1, e.Item2)));

    [TestMethod]
    public void Resolve_SourceWithAnEntry_UsesIt()
    {
        var handling = MetadataSourceSearchSettings.Resolve(
            Overrides(("Hardcover", SearchInitialsHandling.Compact)), "Hardcover", SearchInitialsHandling.Spaced);

        Assert.AreEqual(SearchInitialsHandling.Compact, handling);
    }

    [TestMethod]
    public void Resolve_SourceWithoutAnEntry_FollowsTheLibraryDefault()
    {
        var handling = MetadataSourceSearchSettings.Resolve(
            Overrides(("Hardcover", SearchInitialsHandling.Compact)), "Audible", SearchInitialsHandling.Spaced);

        Assert.AreEqual(SearchInitialsHandling.Spaced, handling);
    }

    [TestMethod]
    public void Resolve_SourceNameIsMatchedIgnoringCase()
    {
        var handling = MetadataSourceSearchSettings.Resolve(
            Overrides(("Hardcover", SearchInitialsHandling.Compact)), "hardcover", SearchInitialsHandling.AsStored);

        Assert.AreEqual(SearchInitialsHandling.Compact, handling);
    }

    [TestMethod]
    public void Resolve_AnExplicitAsStoredOverride_BeatsANonDefaultLibrarySetting()
    {
        // "As stored" for one source is a real choice, not "no choice": it must not fall through
        // to the library-wide Compact.
        var handling = MetadataSourceSearchSettings.Resolve(
            Overrides(("Audible", SearchInitialsHandling.AsStored)), "Audible", SearchInitialsHandling.Compact);

        Assert.AreEqual(SearchInitialsHandling.AsStored, handling);
    }

    [TestMethod]
    public void Resolve_NoOverrides_FollowsTheLibraryDefault()
    {
        Assert.AreEqual(
            SearchInitialsHandling.Spaced,
            MetadataSourceSearchSettings.Resolve(null, "Audible", SearchInitialsHandling.Spaced));
    }

    [TestMethod]
    public void Normalize_TrimsDropsBlanksAndRepeats_FirstSpellingWins()
    {
        var result = MetadataSourceSearchSettings.Normalize(new[]
        {
            KeyValuePair.Create(" Hardcover ", SearchInitialsHandling.Compact),
            KeyValuePair.Create("hardcover", SearchInitialsHandling.Spaced),
            KeyValuePair.Create("  ", SearchInitialsHandling.Spaced),
        });

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(SearchInitialsHandling.Compact, result["Hardcover"]);
    }

    [TestMethod]
    public void Validate_TooManyEntries_Throws()
    {
        var tooMany = Enumerable.Range(0, MetadataSourceSearchSettings.MaxEntries + 1)
            .ToDictionary(i => $"s{i}", _ => SearchInitialsHandling.Compact);

        Assert.ThrowsExactly<ArgumentException>(() => MetadataSourceSearchSettings.Validate(tooMany));
    }

    [TestMethod]
    public void Validate_UndefinedHandling_Throws()
    {
        var bad = new Dictionary<string, SearchInitialsHandling> { ["Audible"] = (SearchInitialsHandling)99 };

        Assert.ThrowsExactly<ArgumentException>(() => MetadataSourceSearchSettings.Validate(bad));
    }

    [TestMethod]
    public void SerializeThenDeserialize_RoundTrips()
    {
        var original = Overrides(("Audible", SearchInitialsHandling.Spaced), ("Hardcover", SearchInitialsHandling.Compact));

        var restored = MetadataSourceSearchSettings.Deserialize(MetadataSourceSearchSettings.Serialize(original));

        Assert.AreEqual(2, restored.Count);
        Assert.AreEqual(SearchInitialsHandling.Spaced, restored["audible"]);
        Assert.AreEqual(SearchInitialsHandling.Compact, restored["HARDCOVER"]);
    }

    [TestMethod]
    public void Serialize_NoOverrides_IsNull()
    {
        Assert.IsNull(MetadataSourceSearchSettings.Serialize(Overrides()));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("[1,2]")]
    public void Deserialize_UnreadableOrEmpty_IsNoOverridesRatherThanAnError(string? json)
    {
        Assert.AreEqual(0, MetadataSourceSearchSettings.Deserialize(json).Count);
    }

    [TestMethod]
    public void Deserialize_DropsAnEntryWhoseValueNoLongerNamesAHandling_KeepsTheRest()
    {
        var restored = MetadataSourceSearchSettings.Deserialize("""{"Audible":"Gone","Hardcover":"Compact","Goodreads":"7"}""");

        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual(SearchInitialsHandling.Compact, restored["Hardcover"]);
    }

    [TestMethod]
    public void LibrarySettings_SearchInitialsHandlingFor_ResolvesPerSourceThenLibraryWide()
    {
        var settings = new LibrarySettings { SearchInitialsHandling = SearchInitialsHandling.Spaced };
        settings.SearchInitialsHandlingBySource["Hardcover"] = SearchInitialsHandling.Compact;

        Assert.AreEqual(SearchInitialsHandling.Compact, settings.SearchInitialsHandlingFor("Hardcover"));
        Assert.AreEqual(SearchInitialsHandling.Spaced, settings.SearchInitialsHandlingFor("Audible"));
    }

    [TestMethod]
    public void LibrarySettings_PathOptions_CombinesNarratorsAndMainAuthorOnly()
    {
        var off = new LibrarySettings { IncludeNarratorInPath = false, MaxNarratorsInPath = 4, MainAuthorOnlyInPath = false };
        var on = new LibrarySettings { IncludeNarratorInPath = true, MaxNarratorsInPath = 4, MainAuthorOnlyInPath = true };

        Assert.AreEqual(new LibraryPathOptions(0, false), off.PathOptions);
        Assert.AreEqual(new LibraryPathOptions(4, true), on.PathOptions);
    }
}
