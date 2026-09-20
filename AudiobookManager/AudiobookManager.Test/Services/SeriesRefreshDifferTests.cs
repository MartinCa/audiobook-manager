using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The series-refresh diff: the explicit PartUpdate / MissingBook / PartRemoval changes a fresh
/// source roster produces against a series' owned books. The matching rule is the reconciliation's
/// own (SeriesRosterMatcher), so the two surfaces agree about what is owned; these tests pin the
/// diff's specific decisions on top of it.
/// </summary>
[TestClass]
public class SeriesRefreshDifferTests
{
    private static SeriesRefreshRosterEntry Roster(string title, string? position = null, int? year = null, bool compilation = false) =>
        new(position, title, year, null, compilation);

    private static SeriesOwnedKey Owned(long id, string? part, string bookName) =>
        new(id, part, bookName);

    private static IReadOnlyList<SeriesRefreshChange> Diff(
        IReadOnlyList<SeriesRefreshRosterEntry> roster,
        IReadOnlyList<SeriesOwnedKey> owned,
        bool includeOmnibus = false) =>
        SeriesRefreshDiffer.Diff(roster, owned, includeOmnibus);

    [TestMethod]
    public void Diff_BookARenumberedTo02_BookBMissing_BookCLosesPart_ProducesUpdateAndRemovalOnly()
    {
        // Book A was stored at part 01 but the source now positions it at 02 -> PartUpdate.
        // Book B exists only on the source roster -> no change any more (see the class doc: a
        // roster entry no owned book matches is never reported here - it's already visible via
        // the roster/reconciliation's Missing/Upcoming sections).
        // Book C carries part 3 but the source no longer lists it -> PartRemoval.
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Book A", "02"),
            Roster("Book B", "4", 2010),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(1, "01", "Book A"),
            Owned(3, "3", "Book C"),
        };

        var changes = Diff(roster, owned);

        Assert.AreEqual(2, changes.Count);
        var update = changes.Single(c => c.Type == SeriesRefreshChangeType.PartUpdate);
        Assert.AreEqual(1, update.AudiobookId);
        Assert.AreEqual("01", update.StoredPart);
        Assert.AreEqual("02", update.NewPart);
        Assert.AreEqual("Book A", update.BookName);
        Assert.AreEqual("Book A", update.RosterTitle);

        Assert.IsFalse(changes.Any(c => c.Type == SeriesRefreshChangeType.MissingBook),
            "a roster entry no owned book matches is never reported as a pending change any more");

        var removal = changes.Single(c => c.Type == SeriesRefreshChangeType.PartRemoval);
        Assert.AreEqual(3, removal.AudiobookId);
        Assert.AreEqual("3", removal.StoredPart);
        Assert.AreEqual("Book C", removal.BookName);
    }

    [TestMethod]
    public void Diff_NoChanges_ReturnsEmpty()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Book A", "1"),
            Roster("Book B", "2"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(1, "1", "Book A"),
            Owned(2, "2", "Book B"),
        };

        var changes = Diff(roster, owned);

        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public void Diff_MatchingPositionDoesNotProposeAnUpdate_EvenWhenTitlesDifferSlightly()
    {
        // "2" and "2.0" are the same part; a position match with a non-contradictory title is
        // the same book, not a candidate.
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("The Well of Ascension", "2"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(7, "2.0", "The Well of Ascension"),
        };

        Assert.AreEqual(0, Diff(roster, owned).Count);
    }

    [TestMethod]
    public void Diff_AgreesWithAnyMatchedEntry_NeverChasesAnotherDuplicateTitle()
    {
        // The book matches both duplicate-title entries (an omnibus and the individual edition
        // sharing one title). Its stored part agrees with one entry, so it is fine - the refresh
        // must not propose a change for a state the reconciliation considers correct.
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("The Final Empire", "1"),
            Roster("The Final Empire", "1.5"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(9, "1.5", "The Final Empire"),
        };

        Assert.AreEqual(0, Diff(roster, owned).Count);
    }

    [TestMethod]
    public void Diff_BookWithNoPartIsNeverProposedAChange()
    {
        // First-time part assignment stays on the detail page's part-mismatch flow; the refresh
        // only renumbers and removes.
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Book A", "2"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(4, null, "Book A"),
        };

        Assert.AreEqual(0, Diff(roster, owned).Count);
    }

    [TestMethod]
    public void Diff_OwnedPartWithNoRosterEntryAtAll_IsARemoval()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Book A", "1"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(1, "1", "Book A"),
            Owned(2, "3", "Book C"),
        };

        var changes = Diff(roster, owned);

        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(SeriesRefreshChangeType.PartRemoval, changes[0].Type);
        Assert.AreEqual(2, changes[0].AudiobookId);
    }

    [TestMethod]
    public void Diff_OmnibusEntriesHiddenWithIncludeOmnibusFalse_DoNotProduceChanges()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Omibus Edition", "1", compilation: true),
            Roster("Individual Book", "1.5"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(1, null, "Omibus Edition"),
            Owned(2, "1.5", "Individual Book"),
        };

        // With omnibus entries hidden the compilation is simply not part of the roster, so it is
        // neither missing nor a part-removal candidate (it carries no part).
        var changes = Diff(roster, owned);

        Assert.AreEqual(0, changes.Count);
    }

    [TestMethod]
    public void Diff_IncludingOmnibusEditions_HiddenCompilationProducesNoChange()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Omnibus Edition", "1", compilation: true),
            Roster("Individual Book", "1.5"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(1, "1.5", "Individual Book"),
        };

        var changes = Diff(roster, owned, includeOmnibus: true);

        Assert.AreEqual(0, changes.Count,
            "an unmatched roster entry is never reported as a pending change - it's already visible via Missing/Upcoming");
    }

    // Regression for the refresh review finding: an ignored roster entry (renumbered by the
    // source or not) and an owned one both produce no changes, same as any other unmatched or
    // matched entry now that MissingBook is never reported - there is no ignore-specific
    // exemption left in Diff itself (ignore decisions survive via the in-place upsert, which
    // never resets IsIgnored - see SeriesService.MatchSeriesCoreAsync).
    [TestMethod]
    public void Diff_UnmatchedEntry_ProducesNoChangeRegardlessOfIgnoreStatus()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Secret History", "3.5"),
            Roster("The Hero of Ages", "3"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            // No part, so it is never a removal candidate (a first-time part is never proposed).
            Owned(1, null, "The Final Empire"),
        };

        var changes = Diff(roster, owned, includeOmnibus: false);

        Assert.AreEqual(0, changes.Count);
    }
}