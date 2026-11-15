using System.Text.Json;
using System.Text.Json.Serialization;
using AudiobookManager.Domain;
using AudiobookManager.Services.Similarity;

namespace AudiobookManager.Services;

/// <summary>One choice the rules cover, with the wording and the options the settings page shows.</summary>
public record MetadataChoiceKind(
    string Key,
    string Label,
    string Description,
    IReadOnlyList<MetadataApplyOption> NamesMainOptions,
    IReadOnlyList<MetadataApplyOption> SilentOptions,
    string? Warning);

/// <summary>
/// The outcome of a choice: <see cref="Value"/> is the pick (null = none for a main author, and "no
/// explicit pick - let the series default decide" for a primary series), and <see cref="NeedsAsk"/>
/// says the rule is Ask me and a real decision exists. When <see cref="NeedsAsk"/> is set,
/// <see cref="Value"/> is what a review pre-selects and what a caller that cannot ask applies.
/// </summary>
public readonly record struct ChoiceOutcome(string? Value, bool NeedsAsk);

/// <summary>
/// The "which one is the main one" rules for applying online metadata: which author is the book's
/// main author and which of its series is the primary one, when a source lists several. They are not
/// field rules (select / overwrite / fill) - they pick among candidates - so they are their own set,
/// with their own settings column and endpoint. The two rules depend on whether the SOURCE designates
/// a main one (Goodreads names a primary contributor, Hardcover a featured series; Audible neither),
/// which is why there is one setting for "the source names it" and one for "the source is silent".
///
/// Defaults (keep the current one both ways) change nothing: a book keeps the main author it has, and
/// the primary series is picked exactly as <see cref="SeriesRelationSet.ChoosePrimary"/> always has.
/// This class owns the lists, the validation, the stored JSON and the resolution; the client holds
/// none of the lists and mirrors only the small resolution (<c>helpers/metadataChoice.ts</c>) because
/// its review dialogs compute their own diffs.
/// </summary>
public sealed class MetadataChoiceRuleSet
{
    public static readonly ChoiceRule DefaultRule = new(SourceNamesMainRule.KeepCurrent, SourceSilentRule.KeepCurrent);

    public static readonly MetadataChoiceRuleSet Defaults = new(new Dictionary<string, ChoiceRule>());

    private const string SeriesWarning =
        "The primary series is part of the library path, so changing it moves the book's file.";

    private const string AuthorWarning =
        "With \"Only the main author in the folder name\" on, changing the main author moves the book's file.";

    private static readonly IReadOnlyList<MetadataApplyOption> NamesMainOptions = new[]
    {
        new MetadataApplyOption(nameof(SourceNamesMainRule.KeepCurrent), "Keep current",
            "Leave the book as it is, even when the source names a different one."),
        new MetadataApplyOption(nameof(SourceNamesMainRule.UseSources), "Use the source's",
            "Take the one the source names."),
        new MetadataApplyOption(nameof(SourceNamesMainRule.AskMe), "Ask me",
            "Send the book to review when the source names a different one than the book has; a review shows a picker."),
    };

    private static readonly IReadOnlyList<MetadataApplyOption> AuthorSilentOptions = new[]
    {
        new MetadataApplyOption(nameof(SourceSilentRule.KeepCurrent), "Keep current",
            "Leave the book's main author alone (cleared only if the source no longer credits them)."),
        new MetadataApplyOption(nameof(SourceSilentRule.FirstListed), "First listed",
            "Use the first author the source lists - unless the book already has a main author the source still credits, which is kept."),
        new MetadataApplyOption(nameof(SourceSilentRule.None), "No main author",
            "Clear the main author."),
        new MetadataApplyOption(nameof(SourceSilentRule.AskMe), "Ask me",
            "Send the book to review when its authors change and it would be left with no main author."),
    };

    private static readonly IReadOnlyList<MetadataApplyOption> SeriesSilentOptions = new[]
    {
        new MetadataApplyOption(nameof(SourceSilentRule.KeepCurrent), "Keep current",
            "Keep the book's primary series when the source still lists it, as always."),
        new MetadataApplyOption(nameof(SourceSilentRule.FirstListed), "First listed",
            "Use the first series the source lists when the book has none of its series among them."),
        new MetadataApplyOption(nameof(SourceSilentRule.AskMe), "Ask me",
            "Send the book to review when none of the book's series is among the source's."),
    };

    /// <summary>Every choice the rules cover, in the order the settings page lists them.</summary>
    public static readonly IReadOnlyList<MetadataChoiceKind> Kinds = new[]
    {
        new MetadataChoiceKind(
            MetadataChoiceKinds.MainAuthor, "Main author",
            "Which of a book's authors is its main author, when a source lists several. Only applied together with the Authors field.",
            NamesMainOptions, AuthorSilentOptions, AuthorWarning),
        new MetadataChoiceKind(
            MetadataChoiceKinds.PrimarySeries, "Primary series",
            "Which of a book's series is its primary one, when a source lists several. Only applied together with the Series field.",
            NamesMainOptions, SeriesSilentOptions, SeriesWarning),
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, ChoiceRule> _rules;

    private MetadataChoiceRuleSet(Dictionary<string, ChoiceRule> rules) => _rules = rules;

    public ChoiceRule Get(string kind) => _rules.TryGetValue(kind, out var rule) ? rule : DefaultRule;

    public IReadOnlyDictionary<string, ChoiceRule> ToDictionary() => _rules;

    /// <summary>Builds a set from stored rules; an unknown choice or an option the choice cannot take falls back to its default.</summary>
    public static MetadataChoiceRuleSet From(IReadOnlyDictionary<string, ChoiceRule>? stored)
    {
        var rules = new Dictionary<string, ChoiceRule>();
        foreach (var kind in Kinds)
        {
            if (stored is not null && stored.TryGetValue(kind.Key, out var rule) && IsAllowed(kind, rule))
            {
                rules[kind.Key] = rule;
            }
        }

        return new MetadataChoiceRuleSet(rules);
    }

    private static bool IsAllowed(MetadataChoiceKind kind, ChoiceRule rule) =>
        Enum.IsDefined(rule.WhenSourceNamesMain)
        && Enum.IsDefined(rule.WhenSourceSilent)
        && kind.SilentOptions.Any(o => o.Key == rule.WhenSourceSilent.ToString());

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> (with a message written for the caller) when a rule is
    /// for an unknown choice or names an option that choice cannot take.
    /// </summary>
    public static void Validate(IReadOnlyDictionary<string, ChoiceRule> proposed)
    {
        foreach (var (key, rule) in proposed)
        {
            var kind = Kinds.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.Ordinal))
                ?? throw new ArgumentException($"'{key}' is not a choice the rules cover.");

            if (!Enum.IsDefined(rule.WhenSourceNamesMain))
            {
                throw new ArgumentException($"'{rule.WhenSourceNamesMain}' is not a known rule for when the source names one ({kind.Label}).");
            }

            if (!Enum.IsDefined(rule.WhenSourceSilent) || kind.SilentOptions.All(o => o.Key != rule.WhenSourceSilent.ToString()))
            {
                throw new ArgumentException($"'{rule.WhenSourceSilent}' is not a rule {kind.Label} can use when the source does not name one.");
            }
        }
    }

    /// <summary>The stored JSON; null when every choice is on its default, so a fresh row stays null.</summary>
    public string? Serialize() =>
        _rules.All(r => r.Value == DefaultRule) ? null : JsonSerializer.Serialize(_rules, JsonOptions);

    /// <summary>Parses the stored JSON; unreadable or absent JSON is "all defaults" rather than an error.</summary>
    public static Dictionary<string, ChoiceRule> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, ChoiceRule>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ChoiceRule>>(json, JsonOptions)
                ?? new Dictionary<string, ChoiceRule>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, ChoiceRule>();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Resolution. Names are compared with NameNormalizer (case, dots and initials spacing folded),
    // because a source spells "George R.R. Martin" where the library has "George R. R. Martin" and
    // that must still be the same author. The value handed back is always the source's spelling,
    // since that is what applying the source's authors stores.
    // ---------------------------------------------------------------------------------------

    /// <summary>Whether two names are the same author or series once case, dots and initials spacing are folded.</summary>
    public static bool SameName(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(NameNormalizer.Normalize(a), NameNormalizer.Normalize(b), StringComparison.Ordinal);

    private static List<string> Distinct(IEnumerable<string?> names)
    {
        var result = new List<string>();
        foreach (var name in names)
        {
            var trimmed = name?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !result.Any(r => SameName(r, trimmed)))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static string? Find(IReadOnlyList<string> candidates, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : candidates.FirstOrDefault(c => SameName(c, name));

    /// <summary>
    /// What a source reported about its authors and series, in the shape the choice rules read: the
    /// authors as applying them would store them (a name that is really several comma-joined people is
    /// split), the series names in the order the source listed them, and the one of each the source
    /// designated as the main one (null when it did not say).
    /// </summary>
    public sealed record ChoiceSource(
        IReadOnlyList<string> Authors, string? MainAuthor, IReadOnlyList<string> Series, string? MainSeries)
    {
        public static ChoiceSource From(Scraping.Models.MetadataSearchResult fetched) => new(
            SplitNames(fetched.Authors.Select(a => a.Name)),
            fetched.MainAuthor,
            (fetched.Series ?? new List<Scraping.Models.MetadataSeriesSearchResult>()).Select(s => s.SeriesName).ToList(),
            fetched.MainSeries);

        public static ChoiceSource From(PendingRefreshPayload.Snapshot snapshot) => new(
            SplitNames(snapshot.Authors),
            snapshot.MainAuthor,
            PendingRefreshPayload.SeriesOf(snapshot).Select(e => e.Name).ToList(),
            PendingRefreshPayload.MainSeriesOf(snapshot));

        private static List<string> SplitNames(IEnumerable<string?> names) =>
            AudiobookManager.FileManager.AudiobookTagHandler
                .SplitCommaJoinedNames(names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => new Person(n!)))
                .Select(p => p.Name)
                .ToList();
    }

    /// <summary>The book's current main author as the rules compare it (null when none).</summary>
    public static string? CurrentMainAuthor(Database.Models.Audiobook book) => book.MainAuthorName;

    /// <summary>The book's current series names, primary first.</summary>
    public static IReadOnlyList<string> CurrentSeries(Database.Models.Audiobook book) =>
        SeriesRelationSet.OfBook(book).All.Select(e => e.Name).ToList();

    /// <summary>
    /// The main author <paramref name="book"/> would have after taking <paramref name="source"/>'s
    /// authors, for the diff and the automated decision.
    /// </summary>
    public ChoiceOutcome ResolveMainAuthor(Database.Models.Audiobook book, ChoiceSource source, string? explicitChoice = null) =>
        ResolveMainAuthor(source.Authors, source.MainAuthor, CurrentMainAuthor(book), explicitChoice);

    /// <summary>The primary series <paramref name="book"/> would take from <paramref name="source"/>'s series.</summary>
    public ChoiceOutcome ResolvePrimarySeries(Database.Models.Audiobook book, ChoiceSource source, string? explicitChoice = null) =>
        ResolvePrimarySeries(source.Series, source.MainSeries, CurrentSeries(book), explicitChoice);

    /// <summary>
    /// The choices an unattended run cannot settle - Ask me with a real decision - among the fields it is
    /// about to apply. A choice only matters when its field is being applied (main author with Authors,
    /// primary series with Series), so a field the apply rules decline never asks. Empty means the run
    /// can go ahead.
    /// </summary>
    public IReadOnlyList<string> AsksFor(Database.Models.Audiobook book, ChoiceSource source, IReadOnlyCollection<string> fieldsToApply)
    {
        var asks = new List<string>();
        if (fieldsToApply.Contains(MetadataRefreshFields.Authors) && ResolveMainAuthor(book, source).NeedsAsk)
        {
            asks.Add(MetadataChoiceKinds.MainAuthor);
        }

        if ((fieldsToApply.Contains(MetadataRefreshFields.Series) || fieldsToApply.Contains(MetadataRefreshFields.SeriesPart))
            && ResolvePrimarySeries(book, source).NeedsAsk)
        {
            asks.Add(MetadataChoiceKinds.PrimarySeries);
        }

        return asks;
    }

    /// <summary>
    /// The main author a book would have after taking <paramref name="sourceAuthors"/>.
    /// <paramref name="sourceMain"/> is the author the source designates (null when it does not say),
    /// <paramref name="currentMain"/> the book's own, and <paramref name="explicitChoice"/> a person's
    /// pick (empty string = no main author, null = no pick; a name the source does not list is ignored).
    /// With fewer than two source authors there is nothing to choose: the current main author is kept
    /// if the source still credits them, otherwise there is none.
    /// </summary>
    public ChoiceOutcome ResolveMainAuthor(
        IEnumerable<string?> sourceAuthors, string? sourceMain, string? currentMain, string? explicitChoice = null)
    {
        var candidates = Distinct(sourceAuthors);
        var keepCurrent = Find(candidates, currentMain);

        if (explicitChoice is not null)
        {
            if (explicitChoice.Length == 0)
            {
                return new ChoiceOutcome(null, false);
            }

            var picked = Find(candidates, explicitChoice);
            if (picked is not null)
            {
                return new ChoiceOutcome(picked, false);
            }
        }

        if (candidates.Count < 2)
        {
            return new ChoiceOutcome(keepCurrent, false);
        }

        var rule = Get(MetadataChoiceKinds.MainAuthor);
        var named = Find(candidates, sourceMain);
        if (named is not null)
        {
            return rule.WhenSourceNamesMain switch
            {
                SourceNamesMainRule.UseSources => new ChoiceOutcome(named, false),
                SourceNamesMainRule.AskMe => new ChoiceOutcome(keepCurrent, !SameName(named, keepCurrent)),
                _ => new ChoiceOutcome(keepCurrent, false),
            };
        }

        return rule.WhenSourceSilent switch
        {
            SourceSilentRule.FirstListed => new ChoiceOutcome(keepCurrent ?? candidates[0], false),
            SourceSilentRule.None => new ChoiceOutcome(null, false),
            SourceSilentRule.AskMe => new ChoiceOutcome(keepCurrent, keepCurrent is null),
            _ => new ChoiceOutcome(keepCurrent, false),
        };
    }

    /// <summary>
    /// The primary series a book would take from <paramref name="sourceSeries"/> (the source's names in
    /// the order it listed them). <paramref name="sourceMain"/> is the series the source designates,
    /// <paramref name="currentSeries"/> the book's own with the primary first, and
    /// <paramref name="explicitChoice"/> a person's pick. <see cref="ChoiceOutcome.Value"/> is the
    /// explicit primary to hand <see cref="SeriesRelationSet.Resolve"/>, or null to let its default decide
    /// (the book's current primary if the source still lists it - which is what Keep current means).
    /// </summary>
    public ChoiceOutcome ResolvePrimarySeries(
        IEnumerable<string?> sourceSeries, string? sourceMain, IReadOnlyList<string> currentSeries, string? explicitChoice = null)
    {
        var candidates = Distinct(sourceSeries);

        var picked = Find(candidates, explicitChoice);
        if (picked is not null)
        {
            return new ChoiceOutcome(picked, false);
        }

        if (candidates.Count < 2)
        {
            return new ChoiceOutcome(null, false);
        }

        var currentPrimary = currentSeries.FirstOrDefault();
        var rule = Get(MetadataChoiceKinds.PrimarySeries);
        var named = Find(candidates, sourceMain);
        if (named is not null)
        {
            return rule.WhenSourceNamesMain switch
            {
                SourceNamesMainRule.UseSources => new ChoiceOutcome(named, false),
                SourceNamesMainRule.AskMe => new ChoiceOutcome(null, !SameName(named, currentPrimary)),
                _ => new ChoiceOutcome(null, false),
            };
        }

        // Silent source: only the case where none of the book's series survives is a real decision;
        // otherwise the default (keep the one the book has) is what every rule would land on.
        var survives = currentSeries.Any(s => Find(candidates, s) is not null);
        return rule.WhenSourceSilent switch
        {
            SourceSilentRule.FirstListed => new ChoiceOutcome(survives ? null : candidates[0], false),
            SourceSilentRule.AskMe => new ChoiceOutcome(null, !survives),
            _ => new ChoiceOutcome(null, false),
        };
    }
}
