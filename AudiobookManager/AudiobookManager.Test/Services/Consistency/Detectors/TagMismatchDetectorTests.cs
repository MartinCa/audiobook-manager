using AudiobookManager.Database.Models;
using AudiobookManager.Services;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainPerson = AudiobookManager.Domain.Person;
using DomainFileInfo = AudiobookManager.Domain.AudiobookFileInfo;

namespace AudiobookManager.Test.Services.Consistency.Detectors;

/// <summary>
/// Regression tests for the TagMismatch issue payload. The expected/actual values are read back
/// by the frontend to render per-field diffs, so the serialization must keep each field's value
/// exactly delimited - including free-text values that contain newline-separated
/// <c>"Field: "</c>-looking lines.
/// </summary>
[TestClass]
public class TagMismatchDetectorTests
{
    private static DbAudiobook MakeDbBook() =>
        new(1, "A Book", null, null, null, 2024,
            null, null, null, null, null, null, null, null, null,
            "/library/author/2024 - A Book/book.m4b", "book.m4b", 1000)
        {
            Authors = new List<Person> { new(1, "Author") },
            Publisher = "Head of Zeus",
        };

    private static DomainAudiobook MakeParsed(string publisher, string description) =>
        new(
            new List<DomainPerson> { new("Author") }, "A Book", 2024,
            new DomainFileInfo("/library/author/2024 - A Book/book.m4b", "book.m4b", 1000))
        {
            Publisher = publisher,
            Description = description,
        };

    [TestMethod]
    public void Detect_MultiLineDescriptionContainingFieldMarkerLine_KeepsDescriptionIntact()
    {
        // Regression: values used to be serialized as newline-separated "Field: value" lines.
        // A description whose text contains a line starting with "Publisher: " was then read as
        // the Publisher field's boundary on the frontend, truncating the description's diff and
        // corrupting the Publisher diff. The payload is JSON now, which escapes the description
        // text, but the reader must never mistake an embedded marker line for a field boundary.
        var description = "A gripping tale.\nPublisher: reprinted by example press\nRead it now.";
        var detector = new TagMismatchDetector();
        var context = new AudiobookCheckContext(
            MakeDbBook(),
            MakeParsed("Macmillan Audio", description),
            "/library/author/2024 - A Book",
            "/library");

        var issues = detector.Detect(context).ToList();

        var issue = issues.Single();
        var expected = TagMismatchPayload.TryParse(issue.ExpectedValue);
        var actual = TagMismatchPayload.TryParse(issue.ActualValue);
        Assert.IsNotNull(expected);
        Assert.IsNotNull(actual);

        var expectedDescription = expected.Single(f => f.Field == "Description").Value;
        var actualDescription = actual.Single(f => f.Field == "Description").Value;
        Assert.AreEqual(description, actualDescription);
        Assert.AreEqual("", expectedDescription);

        Assert.AreEqual("Head of Zeus", expected.Single(f => f.Field == "Publisher").Value);
        Assert.AreEqual("Macmillan Audio", actual.Single(f => f.Field == "Publisher").Value);
    }
}