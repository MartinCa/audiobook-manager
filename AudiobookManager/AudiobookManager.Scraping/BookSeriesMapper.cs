using System.Text.RegularExpressions;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Scraping;

public interface IBookSeriesMapper
{
    public IList<BookSeriesSearchResult> MapBookSeries(IList<BookSeriesSearchResult> results);
}

public class BookSeriesMapper : IBookSeriesMapper
{
    private readonly DatabaseContext _db;

    public BookSeriesMapper(DatabaseContext db)
    {
        _db = db;
    }

    public IList<BookSeriesSearchResult> MapBookSeries(IList<BookSeriesSearchResult> results)
    {
        var mappings = GetRegexMappings();

        return results.Select(x => MapSingleBookSeries(x, mappings)).ToList();
    }

    public BookSeriesSearchResult MapSingleBookSeries(BookSeriesSearchResult result, IList<(Regex CompiledRegex, SeriesMappingDb Mapping)>? mappings = null)
    {
        var allMappings = mappings ?? GetRegexMappings();

        var matchingMapping = allMappings.FirstOrDefault(x => x.CompiledRegex.IsMatch(result.SeriesName));
        if (matchingMapping != default)
        {
            return new BookSeriesSearchResult(matchingMapping.Mapping.MappedSeries)
            {
                OriginalSeriesName = result.SeriesName,
                SeriesPart = result.SeriesPart,
                PartWarning = matchingMapping.Mapping.WarnAboutPart
            };
        }

        return result;
    }

    private IList<(Regex CompiledRegex, SeriesMappingDb Mapping)> GetRegexMappings()
    {
        var mappings = _db.SeriesMappings.ToList();
        return mappings.ConvertAll(x => (CompiledRegex: new Regex(x.Regex, RegexOptions.Compiled), Mapping: x));
    }
}
