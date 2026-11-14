using System.Text;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;

/// <summary>
/// An anthology can credit dozens of authors. Joined into one folder name they exceeded the file
/// system's 255-byte segment limit, so the save failed with PathTooLongException after the tags
/// had already been written. The folder now names as many whole authors as fit; the tags keep all.
/// </summary>
[TestClass]
public class AuthorFolderNameTests
{
    private static readonly char Sep = AudiobookFileHandler.GetDirectorySeparator();

    // The author list of "Octavia's Brood" as it was proposed by the metadata refresh.
    private static readonly string[] OctaviasBroodAuthors =
    {
        "Bao Phi", "David F. Walker", "Alexis Pauline Gumbs", "Morrigan Phillips", "Gabriel Teodros",
        "Tunde Olaniran", "Dawolu Jabari Anderson", "Tara Betts", "Vagabond", "Jelani Wilson",
        "Kalamu ya Salaam", "LeVar Burton", "Terry Bisson", "Dani McClain", "Leah Lakshmi Piepzna-Samarasinha",
        "Mumia Abu-Jamal", "Tananarive Due", "Autumn Brown", "Alixa Garcia", "Mia Mingus",
        "adrienne maree brown", "Walidah Imarisha", "Sheree Renée Thomas",
    };

    private static List<Person> People(params string[] names) => names.Select(n => new Person(n)).ToList();

    private static Audiobook MakeBook(IEnumerable<string> authors, string title = "Octavia's Brood") =>
        new(People(authors.ToArray()), title, 2015, new AudiobookFileInfo("/import/book.m4b", "book.m4b", 1));

    [TestMethod]
    public void ShortList_IsJoinedExactlyAsTheTagIs()
    {
        var folder = AudiobookFileHandler.GetAuthorFolderName(People("Author One", "Author Two"));

        Assert.AreEqual("Author One, Author Two", folder);
    }

    [TestMethod]
    public void ListOverBudget_KeepsWholeNamesInOrderAndNeverCutsOneInHalf()
    {
        var authors = People(OctaviasBroodAuthors);

        var folder = AudiobookFileHandler.GetAuthorFolderName(authors);

        Assert.AreEqual("Bao Phi, David F. Walker, Alexis Pauline Gumbs, Morrigan Phillips, Gabriel Teodros, Tunde Olaniran, Dawolu Jabari Anderson, Tara Betts, Vagabond", folder);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(folder) <= AudiobookFileHandler.MaxAuthorFolderBytes);
    }

    [TestMethod]
    public void ListOverBudget_SkipsAnOverlongNameButKeepsShorterOnesAfterIt()
    {
        var longName = new string('x', 146);

        var folder = AudiobookFileHandler.GetAuthorFolderName(People("Ann", longName, "Bob"));

        Assert.AreEqual($"Ann, Bob", folder);
    }

    [TestMethod]
    public void SingleAuthorOverBudget_IsCutOnACharacterBoundaryWithinTheBudget()
    {
        var folder = AudiobookFileHandler.GetAuthorFolderName(People(new string('é', 200)));

        Assert.IsTrue(Encoding.UTF8.GetByteCount(folder) <= AudiobookFileHandler.MaxAuthorFolderBytes);
        Assert.IsTrue(folder.All(c => c == 'é'), "A multi-byte character was split.");
    }

    [TestMethod]
    public void DuplicateAuthors_AreCountedOnce()
    {
        // The list in the log repeats "adrienne maree brown" and "Walidah Imarisha".
        var folder = AudiobookFileHandler.GetAuthorFolderName(People("Ann", "Bob", "Ann", "Bob"));

        Assert.AreEqual("Ann, Bob", folder);
    }

    [TestMethod]
    public void Regression_OctaviasBrood_GeneratedPathHasNoSegmentOverTheFileSystemLimit()
    {
        var path = AudiobookFileHandler.GenerateRelativeAudiobookPath(MakeBook(OctaviasBroodAuthors), AudiobookManager.Domain.LibraryPathOptions.None);

        foreach (var segment in path.Split(Sep))
        {
            Assert.IsTrue(Encoding.UTF8.GetByteCount(segment) <= AudiobookFileHandler.MaxPathSegmentBytes, segment);
        }

        Assert.IsTrue(path.EndsWith($"{Sep}2015 - Octavia's Brood{Sep}2015 - Octavia's Brood.m4b"), path);
    }

    [TestMethod]
    public void JoinLibraryPath_RefusesASegmentOverTheLimitBeforeAnythingIsWritten()
    {
        var relative = $"Author{Sep}{new string('a', 300)}{Sep}file.m4b";

        var ex = Assert.ThrowsExactly<PathSegmentTooLongException>(
            () => AudiobookFileHandler.JoinLibraryPath("/library", relative));

        StringAssert.Contains(ex.Message, "300 bytes");
        Assert.IsFalse(ex.Message.Contains("/library"), "The message must not carry the container path.");
    }

    [TestMethod]
    public void JoinLibraryPath_MeasuresBytesNotCharacters()
    {
        // 130 two-byte characters: 130 chars but 260 bytes.
        var relative = $"Author{Sep}{new string('é', 130)}{Sep}file.m4b";

        Assert.ThrowsExactly<PathSegmentTooLongException>(() => AudiobookFileHandler.JoinLibraryPath("/library", relative));
    }

    [TestMethod]
    public void JoinLibraryPath_AcceptsASegmentExactlyAtTheLimit()
    {
        var relative = $"Author{Sep}{new string('a', AudiobookFileHandler.MaxPathSegmentBytes)}{Sep}file.m4b";

        var joined = AudiobookFileHandler.JoinLibraryPath("/library", relative);

        Assert.IsTrue(joined.StartsWith("/library"));
    }

    [TestMethod]
    public void JoinLibraryPath_EscapingTheRoot_IsStillAPlainInvalidOperation_NotATooLongName()
    {
        // The two refusals share a base type but mean different things to a caller: only the
        // too-long one is a verdict about the book's metadata.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => AudiobookFileHandler.JoinLibraryPath("/library", "../../escaped.m4b"));

        Assert.IsNotInstanceOfType<PathSegmentTooLongException>(ex);
    }
}
