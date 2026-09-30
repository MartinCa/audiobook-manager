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

    /// <summary>
    /// Folds ASCII letters only, which is exactly what SQLite's NOCASE collation on the unique
    /// index does - a fuller fold here would let two rows through that the index then refuses.
    /// </summary>
    private static string AsciiLower(string value) =>
        string.Create(value.Length, value, (span, v) =>
        {
            for (var i = 0; i < v.Length; i++)
            {
                span[i] = v[i] is >= 'A' and <= 'Z' ? (char)(v[i] + 32) : v[i];
            }
        });

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
            if (seen.Add((AsciiLower(source), AsciiLower(indicator))))
            {
                normalized.Add(new QualifierIndicator(0, source, indicator, qualifier.Key));
            }
        }

        return (await _repository.ReplaceAllAsync(normalized))
            .Select(r => new QualifierIndicatorRule(r.Source, r.Indicator, r.QualifierKey))
            .ToList();
    }
}
