using System.Text.RegularExpressions;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Scraping.Models;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Scraping;

public interface IBookSeriesMapper
{
    public Task<IList<BookSeriesSearchResult>> MapBookSeries(IList<BookSeriesSearchResult> results);
}

public partial class BookSeriesMapper : IBookSeriesMapper
{
    [GeneratedRegex(@"Series$")]
    private static partial Regex ReSeriesEnd();

    private readonly DatabaseContext _db;

    public BookSeriesMapper(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<IList<BookSeriesSearchResult>> MapBookSeries(IList<BookSeriesSearchResult> results)
    {
        var mappings = await GetRegexMappings();

        var mappingTasks = results.Select(x => MapSingleBookSeries(x, mappings));

        await Task.WhenAll(mappingTasks);

        return mappingTasks.Select(task => task.Result).ToList();
    }

    public async Task<BookSeriesSearchResult> MapSingleBookSeries(BookSeriesSearchResult result, IList<(Regex CompiledRegex, SeriesMapping Mapping)>? mappings = null)
    {
        var allMappings = mappings ?? await GetRegexMappings();

        var cleanedResult = CleanSeriesName(result);

        var matchingMapping = allMappings.FirstOrDefault(x => x.CompiledRegex.IsMatch(cleanedResult.SeriesName));
        if (matchingMapping != default)
        {
            return new BookSeriesSearchResult(matchingMapping.Mapping.MappedSeries)
            {
                OriginalSeriesName = cleanedResult.SeriesName,
                SeriesPart = cleanedResult.SeriesPart,
                PartWarning = matchingMapping.Mapping.WarnAboutPart
            };
        }

        return cleanedResult;
    }

    private BookSeriesSearchResult CleanSeriesName(BookSeriesSearchResult result)
    {
        return new BookSeriesSearchResult(ReSeriesEnd().Replace(result.SeriesName, "").Trim())
        {
            OriginalSeriesName = result.OriginalSeriesName,
            SeriesPart = result.SeriesPart,
            PartWarning = result.PartWarning
        };
    }

    private async Task<IList<(Regex CompiledRegex, SeriesMapping Mapping)>> GetRegexMappings()
    {
        var mappings = await _db.SeriesMappings.ToListAsync();
        return mappings.ConvertAll(x => (CompiledRegex: new Regex(x.Regex, RegexOptions.Compiled), Mapping: x));
    }
}
