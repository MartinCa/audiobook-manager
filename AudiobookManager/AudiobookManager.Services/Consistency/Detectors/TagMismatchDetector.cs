using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

public sealed class TagMismatchDetector : IBookConsistencyIssueDetector
{
    public IEnumerable<BookConsistencyIssue> Detect(AudiobookCheckContext context)
    {
        var mismatches = TagConsistencyChecker.FindMismatches(AudiobookService.FromDb(context.Audiobook), context.Parsed);
        if (mismatches.Count == 0)
        {
            yield break;
        }

        var description = $"m4b tags do not match library metadata: {string.Join(", ", mismatches.Select(m => m.Field))}";
        var expectedValue = TagMismatchPayload.Serialize(
            mismatches.Select(m => new TagMismatchPayload.FieldValue(m.Field, m.Expected)));
        var actualValue = TagMismatchPayload.Serialize(
            mismatches.Select(m => new TagMismatchPayload.FieldValue(m.Field, m.Actual)));

        yield return BookConsistencyIssueFactory.Create(context.Audiobook.Id, BookConsistencyIssueType.TagMismatch, description, expectedValue, actualValue);
    }
}
