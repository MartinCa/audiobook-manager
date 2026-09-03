using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public sealed class TagMismatchDetector : IConsistencyIssueDetector
{
    public IEnumerable<ConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        var mismatches = TagConsistencyChecker.FindMismatches(AudiobookService.FromDb(context.Audiobook), context.Parsed);
        if (mismatches.Count == 0)
        {
            yield break;
        }

        var description = $"m4b tags do not match library metadata: {string.Join(", ", mismatches.Select(m => m.Field))}";
        var expectedValue = string.Join("\n", mismatches.Select(m => $"{m.Field}: {m.Expected}"));
        var actualValue = string.Join("\n", mismatches.Select(m => $"{m.Field}: {m.Actual}"));

        yield return ConsistencyIssueFactory.Create(context.Audiobook.Id, ConsistencyIssueType.TagMismatch, description, expectedValue, actualValue);
    }
}
