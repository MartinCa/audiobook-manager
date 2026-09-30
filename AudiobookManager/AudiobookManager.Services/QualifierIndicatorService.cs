using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Scrapers;

namespace AudiobookManager.Services;

public class QualifierIndicatorService : IQualifierIndicatorService
{
    private readonly IQualifierIndicatorRepository _repository;
    private readonly IEnumerable<IScraper> _scrapers;

    public QualifierIndicatorService(IQualifierIndicatorRepository repository, IEnumerable<IScraper> scrapers)
    {
        _repository = repository;
        _scrapers = scrapers;
    }

    public async Task<IReadOnlyList<QualifierIndicatorRule>> GetRulesAsync() =>
        (await _repository.GetAllAsync())
            .Select(r => new QualifierIndicatorRule(r.Source, r.Indicator, r.QualifierKey))
            .ToList();

    public async Task<IReadOnlyList<QualifierIndicatorRule>> ReplaceRulesAsync(IReadOnlyList<QualifierIndicatorRule> rules)
    {
        var sources = _scrapers.Select(s => s.SourceName).ToList();
        var normalized = new List<QualifierIndicator>();
        var seen = new HashSet<(string, string)>();

        foreach (var rule in rules)
        {
            var source = sources.FirstOrDefault(s => string.Equals(s, rule.Source?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"'{rule.Source}' is not a known metadata source.");

            var qualifier = BookQualifiers.Find(rule.QualifierKey)
                ?? throw new ArgumentException($"'{rule.QualifierKey}' is not a known book qualifier.");

            var indicator = QualifierIndicators.NormalizeIndicator(rule.Indicator);
            if (indicator.Length == 0)
            {
                throw new ArgumentException("An indicator cannot be blank.");
            }

            // The same wording twice for one source is one rule; keep the first.
            if (seen.Add((source.ToLowerInvariant(), indicator.ToLowerInvariant())))
            {
                normalized.Add(new QualifierIndicator(0, source, indicator, qualifier.Key));
            }
        }

        return (await _repository.ReplaceAllAsync(normalized))
            .Select(r => new QualifierIndicatorRule(r.Source, r.Indicator, r.QualifierKey))
            .ToList();
    }
}
