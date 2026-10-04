using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Services;

/// <summary>
/// Regression: a refresh whose source reported its authors as one string ("Yuji Oniki, Koushun
/// Takami") produced a single Person with a comma in its name. The tag is comma-joined and read
/// back by splitting, so the file came back as two people and the save's round-trip verification
/// failed on Author ("requested 'Yuji Oniki, Koushun Takami', file has 'Koushun Takami, Yuji
/// Oniki'") on every attempt.
/// </summary>
[TestClass]
public class CommaJoinedPersonNamesTests
{
    private static string Names(IEnumerable<Person> persons) => string.Join("|", persons.Select(p => p.Name));

    [TestMethod]
    public void SplitCommaJoinedNames_OnePersonNamedWithACommaBecomesOnePersonEach_InOrder()
    {
        var result = AudiobookTagHandler.SplitCommaJoinedNames(new[] { new Person("Yuji Oniki, Koushun Takami") });

        Assert.AreEqual("Yuji Oniki|Koushun Takami", Names(result));
    }

    [TestMethod]
    public void SplitCommaJoinedNames_PlainNamesKeepTheirInstanceAndRole_BlanksAndRepeatsAreDropped()
    {
        var plain = new Person("Stephen King") { Id = 7, Role = "Author" };

        var result = AudiobookTagHandler.SplitCommaJoinedNames(new[] { plain, new Person("Stephen King"), new Person(" , ") });

        Assert.AreEqual(1, result.Count);
        Assert.AreSame(plain, result[0]);
    }

    [TestMethod]
    public void ApplyFields_AuthorsFromASourceThatReportsOneCombinedName_AreSplit()
    {
        var book = new Audiobook(
            new List<Person> { new("Old") }, "Battle Royale", 1999, new AudiobookFileInfo("/x.m4b", "x.m4b", 1));
        var fetched = new MetadataSearchResult("https://example.test/b", "Battle Royale")
        {
            Source = "Audible",
            Authors = new List<Person> { new("Yuji Oniki, Koushun Takami") },
            Narrators = new List<Person> { new("A Narrator, B Narrator") },
            Genres = new List<string>(),
        };

        MetadataRefreshApplier.ApplyFields(
            book, PendingRefreshPayload.FromSearchResult(fetched),
            new HashSet<string> { MetadataRefreshFields.Authors, MetadataRefreshFields.Narrators });

        Assert.AreEqual("Yuji Oniki|Koushun Takami", Names(book.Authors));
        Assert.AreEqual("A Narrator|B Narrator", Names(book.Narrators));
    }

    [TestMethod]
    public void SavedTagsRoundTrip_OnceACombinedNameIsSplit_ButNotBefore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "book.m4b");
        File.Copy("FileManager/TestData/fixture.m4b", tempFile);
        try
        {
            var handler = new AudiobookTagHandler(new Mock<ILogger<AudiobookTagHandler>>().Object, new Mock<IAtlLogging>().Object);
            Audiobook Book(List<Person> authors) =>
                new(authors, "Battle Royale", 1999, new AudiobookFileInfo(tempFile, "book.m4b", new FileInfo(tempFile).Length));

            var combined = Book(new List<Person> { new("Yuji Oniki, Koushun Takami") });
            handler.SaveAudiobookTagsToFile(combined);
            var before = TagConsistencyChecker.FindMismatches(combined, handler.ParseAudiobook(new FileInfo(tempFile), false));
            Assert.AreEqual("Author", before.Single().Field, "the unfixed shape must still be the failure this guards against");

            var split = Book(AudiobookTagHandler.SplitCommaJoinedNames(combined.Authors));
            handler.SaveAudiobookTagsToFile(split);
            var after = TagConsistencyChecker.FindMismatches(split, handler.ParseAudiobook(new FileInfo(tempFile), false));
            Assert.AreEqual(0, after.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
