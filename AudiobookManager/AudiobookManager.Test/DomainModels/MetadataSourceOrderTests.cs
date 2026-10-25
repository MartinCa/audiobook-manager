using AudiobookManager.Domain;

namespace AudiobookManager.Test.DomainModels;

[TestClass]
public class MetadataSourceOrderTests
{
    [TestMethod]
    public void Sort_PutsListedSourcesFirstInTheSavedOrder()
    {
        var sorted = MetadataSourceOrder.Sort(new[] { "Audible", "Goodreads", "Hardcover" }, new[] { "Hardcover", "Audible", "Goodreads" });

        CollectionAssert.AreEqual(new[] { "Hardcover", "Audible", "Goodreads" }, sorted);
    }

    [TestMethod]
    public void Sort_UnlistedSourcesFollowAlphabetically_WhateverOrderTheyArrivedIn()
    {
        var sorted = MetadataSourceOrder.Sort(new[] { "Zeta", "Audible", "Goodreads", "Alpha" }, new[] { "Goodreads" });

        CollectionAssert.AreEqual(new[] { "Goodreads", "Alpha", "Audible", "Zeta" }, sorted);
    }

    [TestMethod]
    public void Sort_NoSavedOrder_IsAlphabeticalOrdinal()
    {
        var sorted = MetadataSourceOrder.Sort(new[] { "hardcover", "Audible", "Goodreads" }, Array.Empty<string>());

        // Ordinal: the capitals sort before the lowercase name, deterministically.
        CollectionAssert.AreEqual(new[] { "Audible", "Goodreads", "hardcover" }, sorted);
    }

    [TestMethod]
    public void Sort_MatchesNamesIgnoringCase_AndIgnoresSavedNamesThatAreNotPresent()
    {
        var sorted = MetadataSourceOrder.Sort(new[] { "Audible", "Hardcover" }, new[] { "Removed Source", "HARDCOVER" });

        CollectionAssert.AreEqual(new[] { "Hardcover", "Audible" }, sorted);
    }

    [TestMethod]
    public void Sort_ByKey_OrdersTheItemsNotJustTheNames()
    {
        var items = new[] { (Name: "Audible", Count: 1), (Name: "Hardcover", Count: 2) };

        var sorted = MetadataSourceOrder.Sort(items, i => i.Name, new[] { "Hardcover" });

        CollectionAssert.AreEqual(new[] { 2, 1 }, sorted.Select(i => i.Count).ToList());
    }

    [TestMethod]
    public void Normalize_TrimsDropsBlanksAndRepeats_KeepingTheFirstSpellingAndOrder()
    {
        var normalized = MetadataSourceOrder.Normalize(new[] { " Hardcover", null, "  ", "audible", "AUDIBLE", "Goodreads", "hardcover" });

        CollectionAssert.AreEqual(new[] { "Hardcover", "audible", "Goodreads" }, normalized);
    }

    [TestMethod]
    public void SerializeThenDeserialize_RoundTrips_AndAnEmptyOrderStoresAsNull()
    {
        var json = MetadataSourceOrder.Serialize(new[] { "Hardcover", "Audible" });

        CollectionAssert.AreEqual(new[] { "Hardcover", "Audible" }, MetadataSourceOrder.Deserialize(json));
        Assert.IsNull(MetadataSourceOrder.Serialize(Array.Empty<string>()));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("{\"a\":1}")]
    public void Deserialize_NothingOrUnreadable_IsEmpty(string? json)
    {
        Assert.AreEqual(0, MetadataSourceOrder.Deserialize(json).Count);
    }
}
