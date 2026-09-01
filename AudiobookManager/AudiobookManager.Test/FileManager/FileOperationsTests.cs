using AudiobookManager.FileManager;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.FileManager;

[TestClass]
public class FileOperationsTests
{
    private string _tempDir = null!;
    private Mock<ILogger<FileOperations>> _loggerMock = null!;
    private FileOperations _fileOperations = null!;
    private List<string> _loggedMessages = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fileops-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _loggedMessages = new List<string>();
        _loggerMock = new Mock<ILogger<FileOperations>>();
        _loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                var state = invocation.Arguments[2];
                var formatter = invocation.Arguments[4];
                if (formatter != null)
                {
                    var message = formatter.GetType().GetMethod("Invoke")?.Invoke(formatter, new[] { state, invocation.Arguments[3] }) as string;
                    if (message != null)
                    {
                        _loggedMessages.Add(message);
                    }
                }
                else if (state != null)
                {
                    _loggedMessages.Add(state.ToString()!);
                }
            }));

        _fileOperations = new FileOperations(_loggerMock.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [TestMethod]
    public void WriteAllText_CreatesNewFile_AndLogsCreation()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        _fileOperations.WriteAllText(filePath, "Hello World", "creating note");

        Assert.IsTrue(File.Exists(filePath));
        Assert.AreEqual("Hello World", File.ReadAllText(filePath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Created file") && m.Contains("creating note")));
    }

    [TestMethod]
    public void WriteAllText_OverwritesExistingFile_AndLogsOverwrite()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "Old Content");

        _fileOperations.WriteAllText(filePath, "New Content", "updating note");

        Assert.AreEqual("New Content", File.ReadAllText(filePath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Overwrote file") && m.Contains("updating note")));
    }

    [TestMethod]
    public void WriteAllBytes_CreatesNewFile_AndLogsCreation()
    {
        var filePath = Path.Combine(_tempDir, "test.bin");
        var bytes = new byte[] { 1, 2, 3, 4 };
        _fileOperations.WriteAllBytes(filePath, bytes, "saving binary");

        Assert.IsTrue(File.Exists(filePath));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(filePath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Created file") && m.Contains("saving binary")));
    }

    [TestMethod]
    public void WriteAllBytes_OverwritesExistingFile_AndLogsOverwrite()
    {
        var filePath = Path.Combine(_tempDir, "test.bin");
        File.WriteAllBytes(filePath, new byte[] { 0 });

        var bytes = new byte[] { 5, 6, 7 };
        _fileOperations.WriteAllBytes(filePath, bytes, "updating binary");

        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(filePath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Overwrote file") && m.Contains("updating binary")));
    }

    [TestMethod]
    public void DeleteFile_DeletesFile_AndLogsDeletion()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "content");

        _fileOperations.DeleteFile(filePath, "removing test file");

        Assert.IsFalse(File.Exists(filePath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Deleted file") && m.Contains("removing test file")));
    }

    [TestMethod]
    public void DeleteFileIfExists_FileExists_DeletesAndLogs_ReturnsTrue()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "content");

        var result = _fileOperations.DeleteFileIfExists(filePath, "conditional delete");

        Assert.IsTrue(result);
        Assert.IsFalse(File.Exists(filePath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Deleted file") && m.Contains("conditional delete")));
    }

    [TestMethod]
    public void DeleteFileIfExists_FileDoesNotExist_ReturnsFalse()
    {
        var filePath = Path.Combine(_tempDir, "missing.txt");

        var result = _fileOperations.DeleteFileIfExists(filePath, "conditional delete");

        Assert.IsFalse(result);
        Assert.IsFalse(_loggedMessages.Any(m => m.Contains("Deleted file")));
    }

    [TestMethod]
    public void MoveFile_MovesFile_AndLogsMove()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var destPath = Path.Combine(_tempDir, "dest.txt");
        File.WriteAllText(sourcePath, "moving content");

        _fileOperations.MoveFile(sourcePath, destPath, false, "relocating file");

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.IsTrue(File.Exists(destPath));
        Assert.AreEqual("moving content", File.ReadAllText(destPath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Moved file from") && m.Contains("relocating file")));
    }

    [TestMethod]
    public void CreateDirectory_DirectoryDoesNotExist_CreatesAndLogs()
    {
        var dirPath = Path.Combine(_tempDir, "subdir");

        _fileOperations.CreateDirectory(dirPath, "new directory");

        Assert.IsTrue(Directory.Exists(dirPath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Created directory") && m.Contains("new directory")));
    }

    [TestMethod]
    public void CreateDirectory_DirectoryAlreadyExists_DoesNotLogCreation()
    {
        var dirPath = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(dirPath);

        _fileOperations.CreateDirectory(dirPath, "existing directory");

        Assert.IsFalse(_loggedMessages.Any(m => m.Contains("Created directory")));
    }

    [TestMethod]
    public void DeleteDirectory_DeletesDirectory_AndLogsDeletion()
    {
        var dirPath = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "file.txt"), "inside");

        _fileOperations.DeleteDirectory(dirPath, true, "cleanup subdir");

        Assert.IsFalse(Directory.Exists(dirPath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Deleted directory") && m.Contains("cleanup subdir")));
    }

    [TestMethod]
    public void DeleteDirectoryIfExists_DirectoryExists_DeletesAndLogs_ReturnsTrue()
    {
        var dirPath = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(dirPath);

        var result = _fileOperations.DeleteDirectoryIfExists(dirPath, false, "optional dir delete");

        Assert.IsTrue(result);
        Assert.IsFalse(Directory.Exists(dirPath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Deleted directory") && m.Contains("optional dir delete")));
    }

    [TestMethod]
    public void DeleteDirectoryIfExists_DirectoryDoesNotExist_ReturnsFalse()
    {
        var dirPath = Path.Combine(_tempDir, "missingdir");

        var result = _fileOperations.DeleteDirectoryIfExists(dirPath, false, "optional dir delete");

        Assert.IsFalse(result);
        Assert.IsFalse(_loggedMessages.Any(m => m.Contains("Deleted directory")));
    }

    [TestMethod]
    public void DeleteDirectoryIfEmpty_DirectoryIsEmpty_DeletesAndLogs_ReturnsTrue()
    {
        var dirPath = Path.Combine(_tempDir, "emptydir");
        Directory.CreateDirectory(dirPath);

        var result = _fileOperations.DeleteDirectoryIfEmpty(dirPath, "empty cleanup");

        Assert.IsTrue(result);
        Assert.IsFalse(Directory.Exists(dirPath));
        Assert.IsTrue(_loggedMessages.Any(m => m.Contains("Deleted empty directory") && m.Contains("empty cleanup")));
    }

    [TestMethod]
    public void DeleteDirectoryIfEmpty_DirectoryNotEmpty_DoesNotDelete_ReturnsFalse()
    {
        var dirPath = Path.Combine(_tempDir, "notempty");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "file.txt"), "content");

        var result = _fileOperations.DeleteDirectoryIfEmpty(dirPath, "empty cleanup");

        Assert.IsFalse(result);
        Assert.IsTrue(Directory.Exists(dirPath));
        Assert.IsFalse(_loggedMessages.Any(m => m.Contains("Deleted empty directory")));
    }
}
