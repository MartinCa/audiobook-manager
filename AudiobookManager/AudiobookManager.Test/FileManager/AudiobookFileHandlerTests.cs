using AudiobookManager.FileManager;

namespace AudiobookManager.Test.FileManager;
[TestClass]
public class AudiobookFileHandlerTests
{
    [TestMethod]
    public void GetSafeCombinedPath_Test()
    {
        var pathParts = new List<string> { "test", "test2" };

        var result = AudiobookFileHandler.GetSafeCombinedPath(pathParts);

        Assert.AreEqual($"{pathParts[0]}{Path.DirectorySeparatorChar}{pathParts[1]}", result);
    }
}
