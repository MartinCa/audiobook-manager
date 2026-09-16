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
        bool includeOmnibus = false,
        IReadOnlyList<SeriesRosterMatcher.BookKey>? previouslyIgnored = null) =>
        SeriesRefreshDiffer.Diff(roster, owned, includeOmnibus, previouslyIgnored);

    [TestMethod]
    public void Diff_BookARenumberedTo02_BookBMissing_BookCLosesPart_ProducesAllThreeKinds()
    {
        // Book A was stored at part 01 but the source now positions it at 02 -> PartUpdate.
        // Book B exists only on the source roster -> MissingBook.
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

        Assert.AreEqual(3, changes.Count);
        var update = changes.Single(c => c.Type == SeriesRefreshChangeType.PartUpdate);
        Assert.AreEqual(1, update.AudiobookId);
        Assert.AreEqual("01", update.StoredPart);
        Assert.AreEqual("02", update.NewPart);
        Assert.AreEqual("Book A", update.BookName);
        Assert.AreEqual("Book A", update.RosterTitle);

        var missing = changes.Single(c => c.Type == SeriesRefreshChangeType.MissingBook);
        Assert.IsNull(missing.AudiobookId);
        Assert.AreEqual("4", missing.Position);
        Assert.AreEqual("Book B", missing.Title);
        Assert.AreEqual(2010, missing.Year);

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
    public void Diff_IncludingOmnibusEditions_CountsAHiddenCompilationAsMissing()
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

        Assert.AreEqual(1, changes.Count);
        var missing = changes.Single(c => c.Type == SeriesRefreshChangeType.MissingBook);
        Assert.AreEqual("Omnibus Edition", missing.Title);
    }

    // Regression for the refresh review finding: a roster entry the user has already ignored is
    // deliberately excluded from the visible series, so a refresh must not re-report it as
    // missing - that would contradict the detail page's ignored handling and re-litigate the
    // same decision on every refresh.
    [TestMethod]
    public void Diff_PreviouslyIgnoredEntry_IsNotReportedMissing_WhileRealMissingEntriesStillAre()
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

        var changes = Diff(
            roster,
            owned,
            includeOmnibus: false,
            previouslyIgnored: new List<SeriesRosterMatcher.BookKey>
            {
                SeriesRosterMatcher.BookKey.From("3.5", "Secret History"),
            });

        Assert.AreEqual(1, changes.Count);
        var missing = changes.Single(c => c.Type == SeriesRefreshChangeType.MissingBook);
        Assert.AreEqual("The Hero of Ages", missing.Title, "the real missing entry still surfaces");
    }

    // The exemption uses the same same-book rule the roster replace carries the ignore flags
    // across with: an ignored entry the source has since renumbered (3.5 -> 4) is recognisably
    // the same book and must not start nagging again.
    [TestMethod]
    public void Diff_IgnoredEntryRenumberedByTheSource_IsStillNotReportedMissing()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Secret History", "4"),
        };
        var owned = new List<SeriesOwnedKey>();

        var changes = Diff(
            roster,
            owned,
            includeOmnibus: false,
            previouslyIgnored: new List<SeriesRosterMatcher.BookKey>
            {
                SeriesRosterMatcher.BookKey.From("3.5", "Secret History"),
            });

        Assert.AreEqual(0, changes.Count);
    }

    // An owned entry is unaffected by the previously-ignored exemption - it was never missing.
    [TestMethod]
    public void Diff_PreviouslyIgnoredEntryThatIsNowOwned_ProducesNoChange()
    {
        var roster = new List<SeriesRefreshRosterEntry>
        {
            Roster("Secret History", "3.5"),
        };
        var owned = new List<SeriesOwnedKey>
        {
            Owned(7, "3.5", "Secret History"),
        };

        var changes = Diff(
            roster,
            owned,
            includeOmnibus: false,
            previouslyIgnored: new List<SeriesRosterMatcher.BookKey>
            {
                SeriesRosterMatcher.BookKey.From("3.5", "Secret History"),
            });

        Assert.AreEqual(0, changes.Count);
    }
}