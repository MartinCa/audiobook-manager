using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The "which one is the main one" rules: resolution of a book's main author and primary series when a
/// source lists several, depending on whether the source names a main one.
/// </summary>
[TestClass]
public class MetadataChoiceRuleSetTests
{
    private static MetadataChoiceRuleSet Rules(
        string kind, SourceNamesMainRule named = SourceNamesMainRule.KeepCurrent, SourceSilentRule silent = SourceSilentRule.KeepCurrent) =>
        MetadataChoiceRuleSet.From(new Dictionary<string, ChoiceRule> { [kind] = new(named, silent) });

    private static readonly string[] TwoAuthors = ["A One", "B Two"];

    #region Main author

    [TestMethod]
    public void MainAuthor_Defaults_KeepTheCurrentOneWhetherOrNotTheSourceNamesOne()
    {
        var rules = MetadataChoiceRuleSet.Defaults;

        Assert.AreEqual(new ChoiceOutcome("B Two", false), rules.ResolveMainAuthor(TwoAuthors, "A One", "B Two"));
        Assert.AreEqual(new ChoiceOutcome("B Two", false), rules.ResolveMainAuthor(TwoAuthors, null, "B Two"));
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolveMainAuthor(TwoAuthors, "A One", null));
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolveMainAuthor(TwoAuthors, null, null));
    }

    [TestMethod]
    public void MainAuthor_CurrentOneTheSourceNoLongerCredits_IsDroppedByEveryRuleThatKeeps()
    {
        Assert.AreEqual(
            new ChoiceOutcome(null, false), MetadataChoiceRuleSet.Defaults.ResolveMainAuthor(TwoAuthors, null, "Gone Author"));
    }

    [TestMethod]
    public void MainAuthor_FewerThanTwoSourceAuthors_HasNothingToChoose_NoRuleSetsOne()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources, SourceSilentRule.FirstListed);

        // A sole author is never promoted to main just because a rule says "use the source's".
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolveMainAuthor(["A One"], "A One", null));
        Assert.AreEqual(new ChoiceOutcome("A One", false), rules.ResolveMainAuthor(["A One"], null, "A One"));
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolveMainAuthor([], null, "A One"));
    }

    [TestMethod]
    public void MainAuthor_ExplicitPick_BeatsEveryRule()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.AskMe, SourceSilentRule.AskMe);

        Assert.AreEqual(new ChoiceOutcome("B Two", false), rules.ResolveMainAuthor(TwoAuthors, "A One", "A One", "B Two"));
    }

    [TestMethod]
    public void MainAuthor_ExplicitEmptyPick_MeansNoMainAuthor()
    {
        Assert.AreEqual(
            new ChoiceOutcome(null, false), MetadataChoiceRuleSet.Defaults.ResolveMainAuthor(TwoAuthors, "A One", "B Two", ""));
    }

    [TestMethod]
    public void MainAuthor_ExplicitPickOfSomeoneTheSourceDoesNotList_IsIgnored()
    {
        Assert.AreEqual(
            new ChoiceOutcome("B Two", false),
            MetadataChoiceRuleSet.Defaults.ResolveMainAuthor(TwoAuthors, null, "B Two", "Nobody"));
    }

    [TestMethod]
    public void MainAuthor_SourceNamesOne_UseSourcesTakesIt()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.UseSources);

        Assert.AreEqual(new ChoiceOutcome("A One", false), rules.ResolveMainAuthor(TwoAuthors, "A One", "B Two"));
        Assert.AreEqual(new ChoiceOutcome("A One", false), rules.ResolveMainAuthor(TwoAuthors, "A One", null));
    }

    [TestMethod]
    public void MainAuthor_SourceNamesOne_AskMeAsksOnlyWhenItDiffersFromTheCurrentOne()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.AskMe);

        // Differs: ask, pre-selecting what the book has now.
        Assert.AreEqual(new ChoiceOutcome("B Two", true), rules.ResolveMainAuthor(TwoAuthors, "A One", "B Two"));
        Assert.AreEqual(new ChoiceOutcome(null, true), rules.ResolveMainAuthor(TwoAuthors, "A One", null));
        // Already agrees: nothing to decide.
        Assert.AreEqual(new ChoiceOutcome("A One", false), rules.ResolveMainAuthor(TwoAuthors, "A One", "A One"));
    }

    [TestMethod]
    public void MainAuthor_SourceNamesOne_KeepCurrentIgnoresIt()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.KeepCurrent);

        Assert.AreEqual(new ChoiceOutcome("B Two", false), rules.ResolveMainAuthor(TwoAuthors, "A One", "B Two"));
    }

    [TestMethod]
    public void MainAuthor_SourceSilent_FirstListedFillsAGapButNeverReplacesTheCurrentOne()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, silent: SourceSilentRule.FirstListed);

        Assert.AreEqual(new ChoiceOutcome("A One", false), rules.ResolveMainAuthor(TwoAuthors, null, null));
        Assert.AreEqual(new ChoiceOutcome("B Two", false), rules.ResolveMainAuthor(TwoAuthors, null, "B Two"));
        // The current one is no longer credited, so the gap is filled again.
        Assert.AreEqual(new ChoiceOutcome("A One", false), rules.ResolveMainAuthor(TwoAuthors, null, "Gone Author"));
    }

    [TestMethod]
    public void MainAuthor_SourceSilent_NoneClearsIt()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, silent: SourceSilentRule.None);

        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolveMainAuthor(TwoAuthors, null, "B Two"));
    }

    [TestMethod]
    public void MainAuthor_SourceSilent_AskMeAsksOnlyWhenTheBookWouldBeLeftWithNone()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, silent: SourceSilentRule.AskMe);

        Assert.AreEqual(new ChoiceOutcome("B Two", false), rules.ResolveMainAuthor(TwoAuthors, null, "B Two"));
        Assert.AreEqual(new ChoiceOutcome(null, true), rules.ResolveMainAuthor(TwoAuthors, null, null));
        Assert.AreEqual(new ChoiceOutcome(null, true), rules.ResolveMainAuthor(TwoAuthors, null, "Gone Author"));
    }

    [TestMethod]
    public void MainAuthor_TheRuleThatAppliesDependsOnWhetherTheSourceNamesOne()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources, SourceSilentRule.KeepCurrent);

        Assert.AreEqual("A One", rules.ResolveMainAuthor(TwoAuthors, "A One", "B Two").Value);
        Assert.AreEqual("B Two", rules.ResolveMainAuthor(TwoAuthors, null, "B Two").Value);
    }

    [TestMethod]
    public void MainAuthor_NamesCompareAcrossInitialsSpellings_AndTheSourceSpellingIsReturned()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.KeepCurrent);

        // The book has "George R. R. Martin"; applying the source's authors stores "George R.R. Martin",
        // so the main author has to follow that spelling or it would be dropped.
        var outcome = rules.ResolveMainAuthor(["George R.R. Martin", "Other Person"], null, "George R. R. Martin");

        Assert.AreEqual(new ChoiceOutcome("George R.R. Martin", false), outcome);
    }

    [TestMethod]
    public void MainAuthor_BlankAndRepeatedSourceNames_AreNotCandidates()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, silent: SourceSilentRule.FirstListed);

        // Two entries that are really one author leave one candidate: nothing to choose.
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolveMainAuthor(["A One", "a one", "  ", null], null, null));
    }

    #endregion

    #region Primary series

    private static readonly string[] TwoSeries = ["Saga", "Cosmere"];

    [TestMethod]
    public void PrimarySeries_Defaults_LeaveTheChoiceToTheSeriesDefault()
    {
        var rules = MetadataChoiceRuleSet.Defaults;

        // Value null = no explicit primary, so SeriesRelationSet.ChoosePrimary decides exactly as before.
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(TwoSeries, "Cosmere", ["Saga"]));
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(TwoSeries, null, []));
    }

    [TestMethod]
    public void PrimarySeries_ExplicitPick_Wins()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, SourceNamesMainRule.AskMe, SourceSilentRule.AskMe);

        Assert.AreEqual(new ChoiceOutcome("Cosmere", false), rules.ResolvePrimarySeries(TwoSeries, "Saga", ["Saga"], "Cosmere"));
    }

    [TestMethod]
    public void PrimarySeries_FewerThanTwo_HasNothingToChoose()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, SourceNamesMainRule.UseSources, SourceSilentRule.AskMe);

        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(["Saga"], "Saga", ["Cosmere"]));
    }

    [TestMethod]
    public void PrimarySeries_SourceNamesOne_UseSourcesTakesIt()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, named: SourceNamesMainRule.UseSources);

        Assert.AreEqual(new ChoiceOutcome("Cosmere", false), rules.ResolvePrimarySeries(TwoSeries, "Cosmere", ["Saga"]));
    }

    [TestMethod]
    public void PrimarySeries_SourceNamesOne_KeepCurrentIgnoresIt()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, named: SourceNamesMainRule.KeepCurrent);

        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(TwoSeries, "Cosmere", ["Saga"]));
    }

    [TestMethod]
    public void PrimarySeries_SourceNamesOne_AskMeAsksOnlyWhenItDiffersFromTheCurrentPrimary()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, named: SourceNamesMainRule.AskMe);

        Assert.AreEqual(new ChoiceOutcome(null, true), rules.ResolvePrimarySeries(TwoSeries, "Cosmere", ["Saga"]));
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(TwoSeries, "Saga", ["Saga"]));
    }

    [TestMethod]
    public void PrimarySeries_SourceSilent_FirstListedOnlyActsWhenNoneOfTheBooksSeriesSurvives()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, silent: SourceSilentRule.FirstListed);

        // The book's series is still listed: the default keeps it.
        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(TwoSeries, null, ["Cosmere"]));
        // None survives: the first one the source listed, in the source's order (not alphabetical).
        Assert.AreEqual(new ChoiceOutcome("Saga", false), rules.ResolvePrimarySeries(TwoSeries, null, ["Elsewhere"]));
        Assert.AreEqual(new ChoiceOutcome("Saga", false), rules.ResolvePrimarySeries(TwoSeries, null, []));
    }

    [TestMethod]
    public void PrimarySeries_SourceSilent_AskMeAsksOnlyWhenNoneOfTheBooksSeriesSurvives()
    {
        var rules = Rules(MetadataChoiceKinds.PrimarySeries, silent: SourceSilentRule.AskMe);

        Assert.AreEqual(new ChoiceOutcome(null, false), rules.ResolvePrimarySeries(TwoSeries, null, ["Saga"]));
        Assert.AreEqual(new ChoiceOutcome(null, true), rules.ResolvePrimarySeries(TwoSeries, null, ["Elsewhere"]));
    }

    #endregion

    #region Settings shape

    [TestMethod]
    public void Kinds_CoverMainAuthorAndPrimarySeries_AndOnlyAuthorsMayClearTheChoice()
    {
        CollectionAssert.AreEqual(
            new[] { MetadataChoiceKinds.MainAuthor, MetadataChoiceKinds.PrimarySeries },
            MetadataChoiceRuleSet.Kinds.Select(k => k.Key).ToArray());
        Assert.IsTrue(MetadataChoiceRuleSet.Kinds[0].SilentOptions.Any(o => o.Key == nameof(SourceSilentRule.None)));
        Assert.IsFalse(MetadataChoiceRuleSet.Kinds[1].SilentOptions.Any(o => o.Key == nameof(SourceSilentRule.None)));
    }

    [TestMethod]
    public void Validate_UnknownChoice_IsRefused()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() =>
            MetadataChoiceRuleSet.Validate(new Dictionary<string, ChoiceRule> { ["Narrator"] = MetadataChoiceRuleSet.DefaultRule }));

        StringAssert.Contains(ex.Message, "Narrator");
    }

    [TestMethod]
    public void Validate_PrimarySeriesCannotBeCleared()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            MetadataChoiceRuleSet.Validate(new Dictionary<string, ChoiceRule>
            {
                [MetadataChoiceKinds.PrimarySeries] = new(SourceNamesMainRule.KeepCurrent, SourceSilentRule.None),
            }));
    }

    [TestMethod]
    public void Validate_UndefinedOptions_AreRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            MetadataChoiceRuleSet.Validate(new Dictionary<string, ChoiceRule>
            {
                [MetadataChoiceKinds.MainAuthor] = new((SourceNamesMainRule)99, SourceSilentRule.KeepCurrent),
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            MetadataChoiceRuleSet.Validate(new Dictionary<string, ChoiceRule>
            {
                [MetadataChoiceKinds.MainAuthor] = new(SourceNamesMainRule.KeepCurrent, (SourceSilentRule)99),
            }));
    }

    [TestMethod]
    public void Serialize_AllDefaults_IsNull_SoAFreshRowStaysNull()
    {
        Assert.IsNull(MetadataChoiceRuleSet.Defaults.Serialize());
        Assert.IsNull(Rules(MetadataChoiceKinds.MainAuthor).Serialize());
    }

    [TestMethod]
    public void SerializeThenDeserialize_RoundTripsAsReadableEnumNames()
    {
        var rules = Rules(MetadataChoiceKinds.MainAuthor, SourceNamesMainRule.UseSources, SourceSilentRule.FirstListed);

        var json = rules.Serialize();
        var restored = MetadataChoiceRuleSet.From(MetadataChoiceRuleSet.Deserialize(json));

        StringAssert.Contains(json!, "UseSources");
        Assert.AreEqual(new ChoiceRule(SourceNamesMainRule.UseSources, SourceSilentRule.FirstListed), restored.Get(MetadataChoiceKinds.MainAuthor));
        Assert.AreEqual(MetadataChoiceRuleSet.DefaultRule, restored.Get(MetadataChoiceKinds.PrimarySeries));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("[1]")]
    public void Deserialize_UnreadableOrEmpty_IsAllDefaultsRatherThanAnError(string? json)
    {
        Assert.AreEqual(0, MetadataChoiceRuleSet.Deserialize(json).Count);
    }

    [TestMethod]
    public void From_DropsARuleItsChoiceCannotTake_BackToItsDefault()
    {
        var set = MetadataChoiceRuleSet.From(new Dictionary<string, ChoiceRule>
        {
            [MetadataChoiceKinds.PrimarySeries] = new(SourceNamesMainRule.UseSources, SourceSilentRule.None),
            ["Unknown"] = new(SourceNamesMainRule.UseSources, SourceSilentRule.KeepCurrent),
        });

        Assert.AreEqual(MetadataChoiceRuleSet.DefaultRule, set.Get(MetadataChoiceKinds.PrimarySeries));
        Assert.AreEqual(0, set.ToDictionary().Count);
    }

    #endregion
}
