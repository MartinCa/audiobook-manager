using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;

public interface IQualifierIndicatorRepository
{
    /// <summary>Every rule, ordered by source then indicator, capped at <see cref="QualifierIndicatorRepository.MaxRules"/>.</summary>
    Task<List<QualifierIndicator>> GetAllAsync();

    /// <summary>Replaces the whole rule set atomically and returns what was stored.</summary>
    Task<List<QualifierIndicator>> ReplaceAllAsync(IReadOnlyCollection<QualifierIndicator> rules);
}
