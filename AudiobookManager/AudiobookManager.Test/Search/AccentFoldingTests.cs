using AudiobookManager.Database.Search;

namespace AudiobookManager.Test.Search;

[TestClass]
public class AccentFoldingTests
{
    [TestMethod]
    public void FoldPlain_StripsCombiningDiacritics()
    {
        Assert.AreEqual("Rene", AccentFolding.FoldPlain("René"));
        Assert.AreEqual("Saint-Exupery", AccentFolding.FoldPlain("Saint-Exupéry"));
        Assert.AreEqual("Cafe", AccentFolding.FoldPlain("Café"));
    }

    [TestMethod]
    public void FoldPlain_LeavesUnaccentedTextUnchanged()
    {
        Assert.AreEqual("Rene", AccentFolding.FoldPlain("Rene"));
    }

    [TestMethod]
    public void FoldPlain_NullOrEmpty_ReturnsInputUnchanged()
    {
        Assert.IsNull(AccentFolding.FoldPlain(null));
        Assert.AreEqual(string.Empty, AccentFolding.FoldPlain(string.Empty));
    }

    [TestMethod]
    public void Fold_CalledOutsideAQuery_Throws()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => AccentFolding.Fold("Rene"));
    }
}
