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
}

public partial class BookSeriesMapper : IBookSeriesMapper
{
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
    private readonly Lazy<Task<IList<(Regex CompiledRegex, SeriesMapping Mapping)>>> _mappings;

    public BookSeriesMapper(DatabaseContext db, ILogger<BookSeriesMapper> logger)
    {
        _db = db;
        _logger = logger;
        _mappings = new Lazy<Task<IList<(Regex CompiledRegex, SeriesMapping Mapping)>>>(
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

    public async Task<MetadataSeriesSearchResult> MapSingleBookSeries(MetadataSeriesSearchResult result, IList<(Regex CompiledRegex, SeriesMapping Mapping)>? mappings = null)
    {
        var allMappings = mappings ?? await GetRegexMappings();

        var cleanedResult = CleanSeriesName(result);

        var matchingMapping = allMappings.FirstOrDefault(x => x.CompiledRegex.IsMatch(cleanedResult.SeriesName));
        if (matchingMapping != default)
        {
            return new MetadataSeriesSearchResult(matchingMapping.Mapping.MappedSeries)
            {
                OriginalSeriesName = cleanedResult.SeriesName,
                SeriesPart = cleanedResult.SeriesPart,
                PartWarning = matchingMapping.Mapping.WarnAboutPart
            };
        }

        return cleanedResult;
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

    private Task<IList<(Regex CompiledRegex, SeriesMapping Mapping)>> GetRegexMappings() => _mappings.Value;

    private async Task<IList<(Regex CompiledRegex, SeriesMapping Mapping)>> LoadRegexMappings()
    {
        var mappings = await _db.SeriesMappings.AsNoTracking().ToListAsync();

        // No RegexOptions.Compiled: these are now built once per scope rather than once per call,
        // but a scope is a single request, so the handful of matches a pattern is then used for
        // still does not repay compiling it to IL. That trade would only change if these were
        // cached across requests, which they deliberately are not - see the note on _mappings.
        //
        // A user-supplied pattern that does not compile must not take the whole search result set
        // down with it: every scraped result runs through this, so one bad mapping row otherwise
        // turned every metadata search into a 500 with a regex parse error.
        var compiled = new List<(Regex CompiledRegex, SeriesMapping Mapping)>(mappings.Count);
        foreach (var mapping in mappings)
        {
            try
            {
                compiled.Add((new Regex(mapping.Regex), mapping));
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
