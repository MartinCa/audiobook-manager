using AudiobookManager.Domain;
using AudiobookManager.Services.MappingExtensions;

namespace AudiobookManager.Test.Services;

[TestClass]
public class QueuedOrganizeTaskMappingTests
{
    // The queued organize is serialized to JSON and read back by the worker. A qualifier the
    // round trip dropped would organize the book without its suffix, silently.
    [TestMethod]
    public void ToDb_ThenToDomain_KeepsTheQualifiers()
    {
        var book = new Audiobook(
            new List<Person> { new("Lee Child") }, "Killing Floor", 1997,
            new AudiobookFileInfo("/import/a.m4b", "a.m4b", 1000))
        {
            Series = "Jack Reacher",
            Qualifiers = new List<string> { "abridged", "dramatized" },
        };

        var roundTripped = new QueuedOrganizeTask("/import/a.m4b", book, DateTime.UtcNow).ToDb().ToDomain();

        CollectionAssert.AreEqual(new List<string> { "abridged", "dramatized" }, roundTripped.Audiobook.Qualifiers);
        Assert.AreEqual("Killing Floor", roundTripped.Audiobook.BookName);
        Assert.AreEqual("Jack Reacher", roundTripped.Audiobook.Series);
    }

    [TestMethod]
    public void ToDb_DoesNotSerializeTheDerivedEffectiveNames()
    {
        var book = new Audiobook(
            new List<Person> { new("Lee Child") }, "Killing Floor", 1997,
            new AudiobookFileInfo("/import/a.m4b", "a.m4b", 1000))
        {
            Qualifiers = new List<string> { "dramatized" },
        };

        var json = new QueuedOrganizeTask("/import/a.m4b", book, DateTime.UtcNow).ToDb().JsonAudiobook;

        Assert.IsFalse(json.Contains("EffectiveBookName"), json);
        Assert.IsFalse(json.Contains("Killing Floor (Dramatized)"), json);
    }

    // Rows queued before qualifiers existed have no such property; they must still load, with none.
    [TestMethod]
    public void ToDomain_ARowQueuedBeforeQualifiersExisted_LoadsWithNone()
    {
        var legacy = new AudiobookManager.Database.Models.QueuedOrganizeTask(
            "/import/a.m4b",
            "{\"Audiobook\":{\"Authors\":[{\"Name\":\"Lee Child\"}],\"BookName\":\"Killing Floor\",\"Year\":1997,\"Genres\":[],\"Narrators\":[],\"FileInfo\":{\"FullPath\":\"/import/a.m4b\",\"FileName\":\"a.m4b\",\"SizeInBytes\":1000}},\"MetadataAppliedFromSearch\":false}",
            DateTime.UtcNow);

        var task = legacy.ToDomain();

        Assert.AreEqual(0, task.Audiobook.Qualifiers.Count);
        Assert.AreEqual("Killing Floor", task.Audiobook.BookName);
    }
}
