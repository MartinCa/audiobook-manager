using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MetadataApplyRuleSetTests
{
    private static MetadataApplyRuleSet With(string field, InteractiveApplyRule? interactive = null, AutomatedApplyRule? automated = null) =>
        MetadataApplyRuleSet.From(new Dictionary<string, FieldApplyRule>
        {
            [field] = new FieldApplyRule(
                interactive ?? MetadataApplyRuleSet.DefaultRule.Interactive,
                automated ?? MetadataApplyRuleSet.DefaultRule.Automated),
        });

    private static MetadataRefreshDiff Diff(string field, string? current, string? source) => new(field, current, source);

    [TestMethod]
    public void Defaults_AreSelectAndAskMe_TheBehaviourBeforeTheRulesExisted()
    {
        foreach (var field in MetadataApplyRuleSet.Fields)
        {
            var rule = MetadataApplyRuleSet.Defaults.Get(field.Key);
            Assert.AreEqual(InteractiveApplyRule.AlwaysSelect, rule.Interactive, field.Key);
            Assert.AreEqual(AutomatedApplyRule.AskMe, rule.Automated, field.Key);
        }
    }

    [TestMethod]
    public void Fields_CoverEveryFieldTheRefreshCanOfferExceptTheRetiredSeriesPart()
    {
        // The rules must stay in step with the refresh vocabulary: a field added to
        // MetadataRefreshFields.All without a rule would silently always be "select, ask me".
        CollectionAssert.AreEquivalent(
            MetadataRefreshFields.All.ToList(),
            MetadataApplyRuleSet.Fields.Select(f => f.Key).ToList());
    }

    [TestMethod]
    [DataRow(InteractiveApplyRule.AlwaysSelect, "old", "new", true)]
    [DataRow(InteractiveApplyRule.AlwaysSelect, null, null, true)]
    [DataRow(InteractiveApplyRule.NeverSelect, null, "new", false)]
    [DataRow(InteractiveApplyRule.SelectIfEmpty, null, "new", true)]
    [DataRow(InteractiveApplyRule.SelectIfEmpty, "old", "new", false)]
    [DataRow(InteractiveApplyRule.SelectIfEmpty, null, null, false)]
    [DataRow(InteractiveApplyRule.SelectIfEmpty, null, "  ", false)]
    [DataRow(InteractiveApplyRule.SelectIfSourceHasValue, "old", "new", true)]
    [DataRow(InteractiveApplyRule.SelectIfSourceHasValue, null, "new", true)]
    [DataRow(InteractiveApplyRule.SelectIfSourceHasValue, "old", null, false)]
    public void IsSelectedByDefault_FollowsTheInteractiveRule(InteractiveApplyRule rule, string? current, string? source, bool expected)
    {
        var rules = With(MetadataRefreshFields.Description, interactive: rule);

        Assert.AreEqual(expected, rules.IsSelectedByDefault(MetadataRefreshFields.Description, current, source));
    }

    [TestMethod]
    public void IsSelectedByDefault_AYearOfZeroCountsAsEmpty()
    {
        // The year column is non-nullable, so "no year" is stored as 0.
        var rules = With(MetadataRefreshFields.Year, interactive: InteractiveApplyRule.SelectIfEmpty);

        Assert.IsTrue(rules.IsSelectedByDefault(MetadataRefreshFields.Year, "0", "2020"));
        Assert.IsFalse(rules.IsSelectedByDefault(MetadataRefreshFields.Year, "2019", "2020"));
    }

    [TestMethod]
    [DataRow(AutomatedApplyRule.AlwaysOverwrite, "old", "new", true)]
    [DataRow(AutomatedApplyRule.AlwaysOverwrite, "old", null, true)]
    [DataRow(AutomatedApplyRule.FillBlanksOnly, null, "new", true)]
    [DataRow(AutomatedApplyRule.FillBlanksOnly, "old", "new", false)]
    [DataRow(AutomatedApplyRule.FillBlanksOnly, null, null, false)]
    [DataRow(AutomatedApplyRule.OverwriteUnlessSourceEmpty, "old", "new", true)]
    [DataRow(AutomatedApplyRule.OverwriteUnlessSourceEmpty, null, "new", true)]
    [DataRow(AutomatedApplyRule.OverwriteUnlessSourceEmpty, "old", null, false)]
    [DataRow(AutomatedApplyRule.KeepCurrent, null, "new", false)]
    [DataRow(AutomatedApplyRule.KeepCurrent, "old", "new", false)]
    public void Decide_AppliesTheFieldOnlyWhenItsAutomatedRuleSaysSo(AutomatedApplyRule rule, string? current, string? source, bool expectedApplied)
    {
        var decision = With(MetadataRefreshFields.Description, automated: rule)
            .Decide(new[] { Diff(MetadataRefreshFields.Description, current, source) });

        Assert.IsFalse(decision.RequiresReview);
        Assert.AreEqual(expectedApplied, decision.FieldsToApply.Contains(MetadataRefreshFields.Description));
    }

    [TestMethod]
    public void Decide_AskMeOnADifferingField_SendsTheWholeChangesetToReview_AndAppliesNothing()
    {
        var rules = MetadataApplyRuleSet.From(new Dictionary<string, FieldApplyRule>
        {
            [MetadataRefreshFields.Description] = new(InteractiveApplyRule.AlwaysSelect, AutomatedApplyRule.AlwaysOverwrite),
            [MetadataRefreshFields.Publisher] = new(InteractiveApplyRule.AlwaysSelect, AutomatedApplyRule.AskMe),
        });

        var decision = rules.Decide(new[]
        {
            Diff(MetadataRefreshFields.Description, "old", "new"),
            Diff(MetadataRefreshFields.Publisher, "Old Co", "New Co"),
        });

        Assert.IsTrue(decision.RequiresReview);
        Assert.AreEqual(0, decision.FieldsToApply.Count, "an Always-overwrite field must not apply once the changeset is held for review");
        CollectionAssert.AreEqual(new[] { MetadataRefreshFields.Publisher }, decision.ReviewFields.ToArray());
    }

    [TestMethod]
    public void Decide_AskMeOnAFieldThatDoesNotDiffer_DoesNotHoldTheChangeset()
    {
        var rules = With(MetadataRefreshFields.Description, automated: AutomatedApplyRule.OverwriteUnlessSourceEmpty);

        // Publisher is Ask me by default, but it is not among the differences.
        var decision = rules.Decide(new[] { Diff(MetadataRefreshFields.Description, "old", "new") });

        Assert.IsFalse(decision.RequiresReview);
        CollectionAssert.AreEquivalent(new[] { MetadataRefreshFields.Description }, decision.FieldsToApply.ToArray());
    }

    [TestMethod]
    [DataRow(MetadataRefreshFields.Authors)]
    [DataRow(MetadataRefreshFields.BookName)]
    [DataRow(MetadataRefreshFields.Year)]
    public void Validate_RefusesAlwaysOverwriteOnTheRequiredFields(string field)
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => MetadataApplyRuleSet.Validate(new[]
        {
            KeyValuePair.Create(field, new FieldApplyRule(InteractiveApplyRule.AlwaysSelect, AutomatedApplyRule.AlwaysOverwrite)),
        }));

        StringAssert.Contains(ex.Message, "Always overwrite");
    }

    [TestMethod]
    [DataRow(AutomatedApplyRule.AskMe)]
    [DataRow(AutomatedApplyRule.FillBlanksOnly)]
    [DataRow(AutomatedApplyRule.OverwriteUnlessSourceEmpty)]
    [DataRow(AutomatedApplyRule.KeepCurrent)]
    public void Validate_AllowsEveryOtherAutomatedRuleOnTheRequiredFields(AutomatedApplyRule rule)
    {
        MetadataApplyRuleSet.Validate(new[]
        {
            KeyValuePair.Create(MetadataRefreshFields.Authors, new FieldApplyRule(InteractiveApplyRule.NeverSelect, rule)),
        });
    }

    [TestMethod]
    public void Validate_AllowsAlwaysOverwriteOnSeries_WhichCarriesAWarning()
    {
        MetadataApplyRuleSet.Validate(new[]
        {
            KeyValuePair.Create(MetadataRefreshFields.Series, new FieldApplyRule(InteractiveApplyRule.AlwaysSelect, AutomatedApplyRule.AlwaysOverwrite)),
        });

        var series = MetadataApplyRuleSet.Fields.Single(f => f.Key == MetadataRefreshFields.Series);
        Assert.IsTrue(series.AlwaysOverwriteAllowed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(series.AlwaysOverwriteWarning));
    }

    [TestMethod]
    public void Validate_RefusesAnUnknownField_AndTheRetiredSeriesPart()
    {
        Assert.ThrowsExactly<ArgumentException>(() => MetadataApplyRuleSet.Validate(new[]
        {
            KeyValuePair.Create("Nonsense", MetadataApplyRuleSet.DefaultRule),
        }));
        Assert.ThrowsExactly<ArgumentException>(() => MetadataApplyRuleSet.Validate(new[]
        {
            KeyValuePair.Create(MetadataRefreshFields.SeriesPart, MetadataApplyRuleSet.DefaultRule),
        }));
    }

    [TestMethod]
    public void From_ACombinationTheGuardRailsForbid_FallsBackToAskMeInsteadOfBlankingARequiredField()
    {
        var rules = With(MetadataRefreshFields.Authors, automated: AutomatedApplyRule.AlwaysOverwrite);

        Assert.AreEqual(AutomatedApplyRule.AskMe, rules.Get(MetadataRefreshFields.Authors).Automated);
    }

    [TestMethod]
    public void SerializeAndDeserialize_RoundTripTheRules()
    {
        var rules = MetadataApplyRuleSet.From(new Dictionary<string, FieldApplyRule>
        {
            [MetadataRefreshFields.Description] = new(InteractiveApplyRule.SelectIfEmpty, AutomatedApplyRule.FillBlanksOnly),
            [MetadataRefreshFields.Publisher] = new(InteractiveApplyRule.NeverSelect, AutomatedApplyRule.KeepCurrent),
        });

        var restored = MetadataApplyRuleSet.From(MetadataApplyRuleSet.Deserialize(rules.Serialize()));

        Assert.AreEqual(new FieldApplyRule(InteractiveApplyRule.SelectIfEmpty, AutomatedApplyRule.FillBlanksOnly), restored.Get(MetadataRefreshFields.Description));
        Assert.AreEqual(new FieldApplyRule(InteractiveApplyRule.NeverSelect, AutomatedApplyRule.KeepCurrent), restored.Get(MetadataRefreshFields.Publisher));
        Assert.AreEqual(MetadataApplyRuleSet.DefaultRule, restored.Get(MetadataRefreshFields.Narrators));
    }

    [TestMethod]
    public void Serialize_AllDefaults_IsNull_SoAFreshRowStaysNull()
    {
        Assert.IsNull(MetadataApplyRuleSet.Defaults.Serialize());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("[1,2]")]
    public void Deserialize_AbsentOrUnreadableJson_IsAllDefaults_NotAnError(string? json)
    {
        var rules = MetadataApplyRuleSet.From(MetadataApplyRuleSet.Deserialize(json));

        Assert.AreEqual(MetadataApplyRuleSet.DefaultRule, rules.Get(MetadataRefreshFields.Description));
    }

    [TestMethod]
    public void Options_ExplainEveryRule()
    {
        CollectionAssert.AreEqual(
            Enum.GetNames<InteractiveApplyRule>(), MetadataApplyRuleSet.InteractiveOptions.Select(o => o.Key).ToArray());
        CollectionAssert.AreEqual(
            Enum.GetNames<AutomatedApplyRule>(), MetadataApplyRuleSet.AutomatedOptions.Select(o => o.Key).ToArray());
        Assert.IsTrue(MetadataApplyRuleSet.InteractiveOptions.Concat(MetadataApplyRuleSet.AutomatedOptions)
            .All(o => !string.IsNullOrWhiteSpace(o.Label) && !string.IsNullOrWhiteSpace(o.Description)));
    }
}
