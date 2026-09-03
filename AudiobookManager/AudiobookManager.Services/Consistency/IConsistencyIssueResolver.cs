using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

/// <summary>
/// Fixes one <see cref="ConsistencyIssueType"/> (or a closely related group of them - see
/// <see cref="MetadataSidecarResolver"/> and <see cref="TagOrPathMismatchResolver"/>).
/// Registered as a DI collection (see DependencyInjection.SetupServiceLayer) and dispatched by
/// <see cref="LibraryConsistencyService"/>, which builds a type-to-resolver lookup from
/// <see cref="HandledTypes"/> at construction time - every <see cref="ConsistencyIssueType"/> must
/// have exactly one resolver registered for it, or the dispatch throws rather than silently
/// no-opping.
///
/// The caller (<see cref="LibraryConsistencyService"/>) holds the per-audiobook save gate around
/// the call, once, for whichever resolver runs - a resolver must not acquire it itself.
/// </summary>
public interface IConsistencyIssueResolver
{
    IReadOnlyCollection<ConsistencyIssueType> HandledTypes { get; }

    Task<(ResolveScope Scope, ConsistencyResolveResult Result)> ResolveAsync(ConsistencyIssue issue);
}
