using System.Text.Json;
using System.Text.Json.Serialization;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// One field the apply rules can be set for, with the guard rails the settings page shows.
/// </summary>
/// <param name="Key">The <see cref="MetadataRefreshFields"/> name.</param>
/// <param name="Label">What the settings page calls it.</param>
/// <param name="AlwaysOverwriteAllowed">
/// False for the fields a book cannot sensibly be without (author, title, year): they drive the
/// library path, and <see cref="AutomatedApplyRule.AlwaysOverwrite"/> is the one rule that can
/// replace a value with an empty one.
/// </param>
/// <param name="AlwaysOverwriteWarning">Set for a field where the option is allowed but risky.</param>
public record MetadataApplyField(string Key, string Label, bool AlwaysOverwriteAllowed, string? AlwaysOverwriteWarning);

/// <summary>A selectable rule with the text the settings page explains it with.</summary>
public record MetadataApplyOption(string Key, string Label, string Description);

/// <summary>
/// What an automated run should do with one book's differences: apply <see cref="FieldsToApply"/>,
/// or - when <see cref="RequiresReview"/> - apply nothing and hand the whole changeset to a person.
/// </summary>
public record AutomatedApplyDecision(bool RequiresReview, IReadOnlySet<string> FieldsToApply, IReadOnlyList<string> ReviewFields);

/// <summary>
/// The per-field "how should online metadata be applied" rules, resolved against the defaults.
///
/// Each field has two independent rules, both consulted only when the field differs from the source:
/// an <see cref="InteractiveApplyRule"/> for reviews a person confirms (whether the checkbox starts
/// ticked) and an <see cref="AutomatedApplyRule"/> for unattended runs (what happens with no one
/// there). The default for every field - select it, ask me - is exactly how the app behaved before
/// the rules existed.
///
/// This class owns the field list, the guard rails (<see cref="MetadataApplyField.AlwaysOverwriteAllowed"/>),
/// the JSON stored in the settings row and the evaluation. The client holds none of the lists: it
/// renders what <c>GET api/settings/metadata-apply-rules</c> serves, and mirrors only the tiny
/// interactive evaluation (<c>helpers/metadataApplyRules.ts</c>) because it computes its own diffs.
/// </summary>
public sealed class MetadataApplyRuleSet
{
    public static readonly FieldApplyRule DefaultRule = new(InteractiveApplyRule.AlwaysSelect, AutomatedApplyRule.AskMe);

    private const string SeriesOverwriteWarning =
        "An empty source would remove the book's series, and since the series is part of the library path " +
        "the book's file is moved. Prefer \"Overwrite unless source is empty\" unless you really mean it.";

    /// <summary>Every field the rules cover, in the order the settings page lists them.</summary>
    public static readonly IReadOnlyList<MetadataApplyField> Fields = new[]
    {
        new MetadataApplyField(MetadataRefreshFields.Authors, "Authors", false, null),
        new MetadataApplyField(MetadataRefreshFields.Narrators, "Narrators", true, null),
        new MetadataApplyField(MetadataRefreshFields.BookName, "Book name", false, null),
        new MetadataApplyField(MetadataRefreshFields.Subtitle, "Subtitle", true, null),
        new MetadataApplyField(MetadataRefreshFields.Series, "Series", true, SeriesOverwriteWarning),
        new MetadataApplyField(MetadataRefreshFields.Year, "Year", false, null),
        new MetadataApplyField(MetadataRefreshFields.Genres, "Genres", true, null),
        new MetadataApplyField(MetadataRefreshFields.Description, "Description", true, null),
        new MetadataApplyField(MetadataRefreshFields.Language, "Language", true, null),
        new MetadataApplyField(MetadataRefreshFields.Rating, "Rating", true, null),
        new MetadataApplyField(MetadataRefreshFields.Copyright, "Copyright", true, null),
        new MetadataApplyField(MetadataRefreshFields.Publisher, "Publisher", true, null),
        new MetadataApplyField(MetadataRefreshFields.Asin, "ASIN", true, null),
        new MetadataApplyField(MetadataRefreshFields.Www, "Source URL", true, null),
        new MetadataApplyField(MetadataRefreshFields.Qualifiers, "Qualifiers", true, null),
    };

    public static readonly IReadOnlyList<MetadataApplyOption> InteractiveOptions = new[]
    {
        new MetadataApplyOption(nameof(InteractiveApplyRule.AlwaysSelect), "Always select",
            "The field is ticked whenever it differs. This is how reviews have always worked."),
        new MetadataApplyOption(nameof(InteractiveApplyRule.NeverSelect), "Never select",
            "The field starts unticked; you tick it if you want it."),
        new MetadataApplyOption(nameof(InteractiveApplyRule.SelectIfEmpty), "Select if empty",
            "Ticked only when the book has no value yet and the source has one."),
        new MetadataApplyOption(nameof(InteractiveApplyRule.SelectIfSourceHasValue), "Select if source has value",
            "Ticked only when the source has a value, so an empty source never proposes blanking the field."),
    };

    public static readonly IReadOnlyList<MetadataApplyOption> AutomatedOptions = new[]
    {
        new MetadataApplyOption(nameof(AutomatedApplyRule.AskMe), "Ask me",
            "A difference in this field sends the book's whole changeset to review instead of applying it. " +
            "Nothing for that book is applied automatically, and the review uses the \"when reviewing\" rules."),
        new MetadataApplyOption(nameof(AutomatedApplyRule.AlwaysOverwrite), "Always overwrite",
            "Apply the source's value no matter what either side holds, even if the source is empty."),
        new MetadataApplyOption(nameof(AutomatedApplyRule.FillBlanksOnly), "Fill blanks only",
            "Apply the source's value only when the book has none and the source has one."),
        new MetadataApplyOption(nameof(AutomatedApplyRule.OverwriteUnlessSourceEmpty), "Overwrite unless source is empty",
            "Apply the source's value whenever it has one, replacing what the book holds."),
        new MetadataApplyOption(nameof(AutomatedApplyRule.KeepCurrent), "Keep current",
            "Never apply the source's value, whatever the book holds."),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IReadOnlyDictionary<string, FieldApplyRule> _rules;

    private MetadataApplyRuleSet(IReadOnlyDictionary<string, FieldApplyRule> rules)
    {
        _rules = rules;
    }

    /// <summary>Every field on its default rule: the pre-rules behaviour.</summary>
    public static MetadataApplyRuleSet Defaults { get; } = new(Fields.ToDictionary(f => f.Key, _ => DefaultRule));

    /// <summary>
    /// Resolves stored rules against the defaults, leniently: an unknown field is dropped, a missing
    /// one takes the default, and a combination the guard rails forbid (a hand-edited row, or a rule
    /// set stored before a field became guarded) falls back to <see cref="AutomatedApplyRule.AskMe"/>
    /// rather than ever writing an empty value into a field that must keep one.
    /// </summary>
    public static MetadataApplyRuleSet From(IReadOnlyDictionary<string, FieldApplyRule>? stored)
    {
        var resolved = new Dictionary<string, FieldApplyRule>();
        foreach (var field in Fields)
        {
            var rule = stored is not null && stored.TryGetValue(field.Key, out var found) ? found : DefaultRule;
            if (!Enum.IsDefined(rule.Interactive))
            {
                rule = rule with { Interactive = DefaultRule.Interactive };
            }

            if (!Enum.IsDefined(rule.Automated) || (rule.Automated == AutomatedApplyRule.AlwaysOverwrite && !field.AlwaysOverwriteAllowed))
            {
                rule = rule with { Automated = DefaultRule.Automated };
            }

            resolved[field.Key] = rule;
        }

        return new MetadataApplyRuleSet(resolved);
    }

    /// <summary>
    /// Checks rules a caller wants to save. Unlike <see cref="From"/> this is strict: it names what
    /// is wrong, so the settings page can say so.
    /// </summary>
    /// <exception cref="ArgumentException">Carries a message that is safe to show the caller.</exception>
    public static void Validate(IEnumerable<KeyValuePair<string, FieldApplyRule>> proposed)
    {
        foreach (var (key, rule) in proposed)
        {
            var field = Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal))
                ?? throw new ArgumentException($"'{key}' is not a field the apply rules cover.");

            if (!Enum.IsDefined(rule.Interactive))
            {
                throw new ArgumentException($"'{rule.Interactive}' is not a known \"when reviewing\" rule for {field.Label}.");
            }

            if (!Enum.IsDefined(rule.Automated))
            {
                throw new ArgumentException($"'{rule.Automated}' is not a known \"when automated\" rule for {field.Label}.");
            }

            if (rule.Automated == AutomatedApplyRule.AlwaysOverwrite && !field.AlwaysOverwriteAllowed)
            {
                throw new ArgumentException(
                    $"{field.Label} cannot be set to \"Always overwrite\": it is required, and an empty source would blank it.");
            }
        }
    }

    public FieldApplyRule Get(string field) => _rules.TryGetValue(field, out var rule) ? rule : DefaultRule;

    public IReadOnlyDictionary<string, FieldApplyRule> ToDictionary() => _rules;

    /// <summary>The stored JSON; null when every field is on its default, so a fresh row stays null.</summary>
    public string? Serialize() =>
        _rules.All(r => r.Value == DefaultRule) ? null : JsonSerializer.Serialize(_rules, JsonOptions);

    /// <summary>Parses the stored JSON; unreadable or absent JSON is "all defaults" rather than an error.</summary>
    public static Dictionary<string, FieldApplyRule> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, FieldApplyRule>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FieldApplyRule>>(json, JsonOptions)
                ?? new Dictionary<string, FieldApplyRule>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, FieldApplyRule>();
        }
    }

    /// <summary>
    /// "Empty" for a diff side: no text. A year of 0 is the stored book's way of saying none (the
    /// column is non-nullable). Differ values arrive already trimmed to null when blank.
    /// </summary>
    public static bool IsEmpty(string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (field == MetadataRefreshFields.Year && value.Trim() == "0");

    /// <summary>
    /// Whether a person's review should start with this field ticked. The client mirrors this for
    /// the diff it computes itself (<c>helpers/metadataApplyRules.ts</c>).
    /// </summary>
    public bool IsSelectedByDefault(string field, string? libraryValue, string? sourceValue)
    {
        var currentEmpty = IsEmpty(field, libraryValue);
        var sourceEmpty = IsEmpty(field, sourceValue);
        return Get(field).Interactive switch
        {
            InteractiveApplyRule.NeverSelect => false,
            InteractiveApplyRule.SelectIfEmpty => currentEmpty && !sourceEmpty,
            InteractiveApplyRule.SelectIfSourceHasValue => !sourceEmpty,
            _ => true,
        };
    }

    /// <summary>
    /// What an unattended run does with a book's differences. Any <see cref="AutomatedApplyRule.AskMe"/>
    /// field in the changeset sends the whole book to review - none of the other fields' rules take
    /// effect then, not even the ones that would have applied - so the person sees one coherent
    /// changeset rather than a half-applied one.
    /// </summary>
    public AutomatedApplyDecision Decide(IEnumerable<MetadataRefreshDiff> differences)
    {
        var diffs = differences.ToList();
        var reviewFields = diffs
            .Where(d => Get(d.Field).Automated == AutomatedApplyRule.AskMe)
            .Select(d => d.Field)
            .ToList();
        if (reviewFields.Count > 0)
        {
            return new AutomatedApplyDecision(true, new HashSet<string>(), reviewFields);
        }

        var apply = new HashSet<string>();
        foreach (var diff in diffs)
        {
            var currentEmpty = IsEmpty(diff.Field, diff.LibraryValue);
            var sourceEmpty = IsEmpty(diff.Field, diff.SourceValue);
            var applies = Get(diff.Field).Automated switch
            {
                AutomatedApplyRule.AlwaysOverwrite => true,
                AutomatedApplyRule.FillBlanksOnly => currentEmpty && !sourceEmpty,
                AutomatedApplyRule.OverwriteUnlessSourceEmpty => !sourceEmpty,
                _ => false,
            };

            if (applies)
            {
                apply.Add(diff.Field);
            }
        }

        return new AutomatedApplyDecision(false, apply, Array.Empty<string>());
    }
}
