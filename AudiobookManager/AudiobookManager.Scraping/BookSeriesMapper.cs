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

    public BookSeriesMapper(DatabaseContext db, ILogger<BookSeriesMapper> logger)
    {
        _db = db;
        _logger = logger;
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

    private async Task<IList<(Regex CompiledRegex, SeriesMapping Mapping)>> GetRegexMappings()
    {
        var mappings = await _db.SeriesMappings.AsNoTracking().ToListAsync();

        // No RegexOptions.Compiled: these are rebuilt on every call rather than cached, and
        // compiling a pattern to IL costs far more than the handful of matches it is then used
        // for - so it was paying the compile price once per search and never collecting on it.
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
