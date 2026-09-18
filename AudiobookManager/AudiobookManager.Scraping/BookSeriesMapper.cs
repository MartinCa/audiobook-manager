using System.Text.RegularExpressions;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Scraping.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AudiobookManager.Scraping;

public interface IBookSeriesMapper
{
    public Task<IList<MetadataSeriesSearchResult>> MapBookSeries(IList<MetadataSeriesSearchResult> results);

    /// <summary>
    /// Maps each book's series list through the user's series mappings, preserving the grouping:
    /// output group i is input group i, entry-for-entry. <see cref="MapSingleBookSeries"/> is a
    /// strict 1:1, order-preserving function of each input (never filters, merges, or reorders),
    /// and this method's shape holds callers to that per book instead of letting them slice one
    /// flattened result on trust. Callers mapping more than one book's series at once use this
    /// rather than flattening and re-slicing.
    /// </summary>
    public Task<IList<IList<MetadataSeriesSearchResult>>> MapBookSeriesPerBook(IList<IList<MetadataSeriesSearchResult>> books);
}

public partial class BookSeriesMapper : IBookSeriesMapper
{
    /// <summary>
    /// One mapping compiled for this scope. Mutable in one respect only: <see cref="Disabled"/>
    /// is set when the pattern blows its match timeout, which takes it out of the rest of this
    /// scope's scans.
    ///
    /// That write races, and is deliberately left unsynchronized. MapBookSeries fans its results
    /// out with Task.WhenAll over one shared mapping list, so two results can both reach the same
    /// runaway pattern before either sets the flag. A bool write cannot tear, and the value only
    /// ever goes false -> true, so the worst a lost race costs is one extra timeout - never a
    /// wrong mapping. A lock here would serialize every match to save that.
    /// </summary>
    private sealed class CompiledMapping(Regex compiledRegex, SeriesMapping mapping, string targetSeriesName)
    {
        public Regex CompiledRegex { get; } = compiledRegex;
        public SeriesMapping Mapping { get; } = mapping;
        public string TargetSeriesName { get; } = targetSeriesName;
        public bool Disabled { get; set; }
    }

    /// <summary>
    /// The most mapping patterns one scope will match against. Patterns are hand-entered on the
    /// series detail page, so a real library stays far under this; it bounds the per-result scan
    /// so the set cannot grow into a cost multiplier, in the same spirit as the repository's
    /// per-series cap. Ordered by id, so which patterns survive the cap is stable rather than
    /// whatever the database happened to return.
    /// </summary>
    public const int MaxMappings = 1_000;

    [GeneratedRegex(@"Series$", RegexOptions.IgnoreCase)]
    private static partial Regex ReSeriesEnd();

    private readonly DatabaseContext _db;
    private readonly ILogger<BookSeriesMapper> _logger;

    /// <summary>
    /// The compiled mappings for this scope, loaded exactly once.
    ///
    /// Every scraper runs its results through here, and several do so from a fan-out:
    /// AudibleScraper.Search starts one parse task per search hit and awaits them with
    /// Task.WhenAll, and ScrapingService.SearchMultiple runs the three scrapers concurrently -
    /// which share this instance, since it is registered scoped. Loading inside each call meant
    /// one SELECT per search result (20 identical queries for one Audible search), and, worse,
    /// several of them potentially in flight at once against a DbContext that permits exactly one
    /// operation at a time. That has not thrown in practice only because Microsoft.Data.Sqlite
    /// completes its async methods synchronously, so the queries never actually overlap - a
    /// property of the provider, not of this code, and not one worth resting on.
    ///
    /// Lazy with ExecutionAndPublication runs the factory once even under concurrent first calls;
    /// everyone else awaits the same Task. Per-scope rather than cached longer, so a mapping the
    /// user just edited is picked up by the next request.
    /// </summary>
    private readonly Lazy<Task<IList<CompiledMapping>>> _mappings;

    public BookSeriesMapper(DatabaseContext db, ILogger<BookSeriesMapper> logger)
    {
        _db = db;
        _logger = logger;
        _mappings = new Lazy<Task<IList<CompiledMapping>>>(
            LoadRegexMappings, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<IList<MetadataSeriesSearchResult>> MapBookSeries(IList<MetadataSeriesSearchResult> results)
    {
        var mappings = await GetRegexMappings();

        // Materialize once. `Select` is lazy, so awaiting the sequence and then reading .Result
        // off it enumerated it a second time - starting a whole second set of mappings and
        // blocking on those instead of the ones that had been awaited.
        var mappingTasks = results.Select(x => MapSingleBookSeries(x, mappings)).ToList();

        var mapped = await Task.WhenAll(mappingTasks);

        return mapped.ToList();
    }

    public async Task<IList<IList<MetadataSeriesSearchResult>>> MapBookSeriesPerBook(IList<IList<MetadataSeriesSearchResult>> books)
    {
        // Each group goes through MapBookSeries, which is entry-for-entry 1:1 within the group
        // (Select + Task.WhenAll preserve count and order), so the group correspondence this
        // method's contract promises is structural, not incidental. Every group shares the one
        // lazy mappings load; the mapping itself is pure CPU after that, so the concurrent
        // groups never touch the DbContext.
        var mappedGroups = await Task.WhenAll(books.Select(MapBookSeries));

        return mappedGroups.ToList();
    }

    /// <summary>
    /// Maps one result's series value. The public single-argument form is what external callers
    /// use; MapBookSeries passes the already-loaded set so a fan-out does not re-resolve it.
    /// </summary>
    public async Task<MetadataSeriesSearchResult> MapSingleBookSeries(MetadataSeriesSearchResult result) =>
        await MapSingleBookSeries(result, null);

    private async Task<MetadataSeriesSearchResult> MapSingleBookSeries(MetadataSeriesSearchResult result, IList<CompiledMapping>? mappings)
    {
        var allMappings = mappings ?? await GetRegexMappings();

        var cleanedResult = CleanSeriesName(result);

        var matchingMapping = FirstMatch(allMappings, cleanedResult.SeriesName);
        if (matchingMapping is not null)
        {
            return new MetadataSeriesSearchResult(matchingMapping.TargetSeriesName)
            {
                OriginalSeriesName = cleanedResult.SeriesName,
                SeriesPart = cleanedResult.SeriesPart,
                PartWarning = matchingMapping.Mapping.WarnAboutPart
            };
        }

        return cleanedResult;
    }

    /// <summary>
    /// The first mapping whose pattern matches, in id order (first-match wins - the unique index
    /// on the pattern, plus the ordered load, is what makes that deterministic).
    ///
    /// A pattern that blows SeriesMappingPattern.MatchTimeout is treated as not matching, and the
    /// scan carries on with the rest. These are user-authored patterns run against every scraped
    /// result, so one catastrophically backtracking row must cost its own mapping and nothing
    /// else; without the timeout it wedged the request thread outright, and failing the whole
    /// search instead would hand one bad row the same power for a different reason.
    ///
    /// Such a pattern is also disabled for the remainder of the scope - see the catch below for
    /// why skipping it per result is not enough.
    /// </summary>
    private CompiledMapping? FirstMatch(
        IList<CompiledMapping> mappings,
        string seriesName)
    {
        foreach (var mapping in mappings)
        {
            if (mapping.Disabled)
            {
                continue;
            }

            try
            {
                if (mapping.CompiledRegex.IsMatch(seriesName))
                {
                    return mapping;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                // Disabled for the rest of this scope, not just skipped for this result. The
                // timeout is per match and this scan runs once per scraped result, so a runaway
                // pattern that stayed enabled would charge its full timeout on every result of
                // every search in the request - N bad patterns x N results. A pattern that cannot
                // answer within the timeout has no answer to give, so dropping it after the first
                // proof of that costs nothing but the one timeout.
                mapping.Disabled = true;
                _logger.LogWarning(ex,
                    "Series mapping {MappingId} ('{Pattern}') timed out matching '{SeriesName}' and is disabled for this request; the pattern backtracks catastrophically and should be simplified",
                    mapping.Mapping.Id, mapping.Mapping.Regex, seriesName);
            }
        }

        return null;
    }

    private MetadataSeriesSearchResult CleanSeriesName(MetadataSeriesSearchResult result)
    {
        return new MetadataSeriesSearchResult(ReSeriesEnd().Replace(result.SeriesName, "").Trim())
        {
            OriginalSeriesName = result.OriginalSeriesName,
            SeriesPart = result.SeriesPart,
            PartWarning = result.PartWarning
        };
    }

    private Task<IList<CompiledMapping>> GetRegexMappings() => _mappings.Value;

    private async Task<IList<CompiledMapping>> LoadRegexMappings()
    {
        // Mappings are owned by a Series row now: the target is always the owner's name, never a
        // value on the mapping itself. The Include resolves it in one LEFT JOIN, so this stays a
        // single read of the table (the BookSeriesMapperTests counters assert exactly that).
        //
        // Bounded at the query boundary (MaxMappings + 1 rows, so the overflow is detectable
        // without transferring the excess): this set is scanned once per scraped result, so an
        // unbounded read makes its size a multiplier on every metadata search. Ordered by id so
        // the surviving patterns are stable rather than whatever order the database returned.
        var mappings = await _db.SeriesMappings
            .AsNoTracking()
            .Include(m => m.Series)
            .OrderBy(m => m.Id)
            .Take(MaxMappings + 1)
            .ToListAsync();

        if (mappings.Count > MaxMappings)
        {
            // Truncated rather than refused: a metadata search that returns unmapped series values
            // is far better than one that fails outright, and the cap is far above any
            // hand-maintained set. The log names the cap so the cause is not a mystery.
            _logger.LogWarning(
                "More than {MaxMappings} series mapping patterns exist; only the first {MaxMappings} (by id) are applied to this request's metadata results",
                MaxMappings, MaxMappings);
            mappings = mappings.Take(MaxMappings).ToList();
        }

        // No RegexOptions.Compiled: these are now built once per scope rather than once per call,
        // but a scope is a single request, so the handful of matches a pattern is then used for
        // still does not repay compiling it to IL. That trade would only change if these were
        // cached across requests, which they deliberately are not - see the note on _mappings.
        //
        // SeriesMappingPattern.Compile applies the per-match timeout that bounds a catastrophically
        // backtracking pattern; see that class for why these two failure modes are handled here
        // rather than trusted to the pattern's author.
        //
        // A user-supplied pattern that does not compile must not take the whole search result set
        // down with it: every scraped result runs through this, so one bad mapping row otherwise
        // turned every metadata search into a 500 with a regex parse error. The write endpoints
        // reject such a pattern up front now, so reaching this is a row that predates that check.
        var compiled = new List<CompiledMapping>(mappings.Count);
        foreach (var mapping in mappings)
        {
            var targetSeriesName = mapping.Series?.Name;
            if (string.IsNullOrWhiteSpace(targetSeriesName))
            {
                // A mapping row whose owner row is missing (should not happen - the FK is
                // required and cascade-deletes with its series) can only map to nothing.
                continue;
            }

            try
            {
                compiled.Add(new CompiledMapping(SeriesMappingPattern.Compile(mapping.Regex), mapping, targetSeriesName));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex,
                    "Skipping series mapping {MappingId}: '{Pattern}' is not a valid regular expression",
                    mapping.Id, mapping.Regex);
            }
        }

        return compiled;
    }
}
