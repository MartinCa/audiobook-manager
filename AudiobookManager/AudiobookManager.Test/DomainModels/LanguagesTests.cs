using AudiobookManager.Domain;

// Deliberately not `AudiobookManager.Test.Domain`: that would shadow AudiobookManager.Domain
// for every test in this assembly that refers to a domain type as `Domain.Audiobook`.
namespace AudiobookManager.Test.DomainModels;

[TestClass]
public class LanguagesTests
{
    [TestMethod]
    [DataRow("en")]
    [DataRow("EN")]
    [DataRow("eng")]
    [DataRow("English")]
    [DataRow("english")]
    [DataRow("  English  ")]
    [DataRow("en-US")]
    [DataRow("en_GB")]
    public void Normalize_FoldsEveryEnglishSpellingToTheIso6391Code(string raw)
    {
        Assert.AreEqual("en", Languages.Normalize(raw));
    }

    [TestMethod]
    [DataRow("da")]
    [DataRow("DA")]
    [DataRow("dan")]
    [DataRow("Danish")]
    [DataRow("Dansk")]
    [DataRow("da-DK")]
    public void Normalize_FoldsEveryDanishSpellingToTheIso6391Code(string raw)
    {
        Assert.AreEqual("da", Languages.Normalize(raw));
    }

    [TestMethod]
    [DataRow("German")]
    [DataRow("de")]
    [DataRow("xx")]
    [DataRow("Swedish")]
    public void Normalize_ReturnsNullForALanguageTheLibraryDoesNotManage(string raw)
    {
        Assert.IsNull(Languages.Normalize(raw));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void Normalize_ReturnsNullForAnEmptyValue(string? raw)
    {
        Assert.IsNull(Languages.Normalize(raw));
    }

    [TestMethod]
    public void Supported_ContainsExactlyEnglishAndDanish()
    {
        CollectionAssert.AreEqual(
            new List<string> { "en", "da" },
            Languages.Supported.Select(l => l.Code).ToList());
        CollectionAssert.AreEqual(
            new List<string> { "English", "Danish" },
            Languages.Supported.Select(l => l.DisplayName).ToList());
    }

    [TestMethod]
    public void DefaultCode_IsEnglishAndIsItselfSupported()
    {
        // Read through a local so the analyzer doesn't fold the const away as a constant
        // comparison - the point of the assertion is that the default is English.
        var defaultCode = Languages.DefaultCode;

        Assert.AreEqual("en", defaultCode);
        Assert.IsTrue(
            Languages.Supported.Any(l => l.Code == defaultCode),
            "The default has to be one of the supported options");
    }

    [TestMethod]
    public void IsSupported_RejectsUnmanagedAndEmptyCodes()
    {
        Assert.IsFalse(Languages.IsSupported("de"));
        Assert.IsFalse(Languages.IsSupported(null));
        // Normalize lowercases before matching; IsSupported tests a stored code, which is always
        // already lowercase, so an uppercase one is genuinely not a value this library stores.
        Assert.IsFalse(Languages.IsSupported("EN"));
    }

    [TestMethod]
    public void DisplayName_FallsBackToTheCodeForAnUnmanagedLanguage()
    {
        Assert.AreEqual("English", Languages.DisplayName("en"));
        Assert.AreEqual("Danish", Languages.DisplayName("da"));
        Assert.AreEqual("de", Languages.DisplayName("de"));
    }
}
