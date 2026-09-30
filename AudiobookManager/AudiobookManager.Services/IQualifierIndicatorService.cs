using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface IQualifierIndicatorService
{
    /// <summary>Every configured rule (bounded - see <c>QualifierIndicatorRepository.MaxRules</c>).</summary>
    Task<IReadOnlyList<QualifierIndicatorRule>> GetRulesAsync();

    /// <summary>
    /// Replaces the whole rule set. Indicators are normalized and de-duplicated per source;
    /// throws <see cref="ArgumentException"/> (safe to show the caller) for an unknown source, an
    /// unknown qualifier, or a blank indicator.
    /// </summary>
    Task<IReadOnlyList<QualifierIndicatorRule>> ReplaceRulesAsync(IReadOnlyList<QualifierIndicatorRule> rules);
}
