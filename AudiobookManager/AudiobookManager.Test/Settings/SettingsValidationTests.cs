using AudiobookManager.Settings;

namespace AudiobookManager.Test.Settings;

[TestClass]
public class SettingsValidationTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"settings-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "import"));
        Directory.CreateDirectory(Path.Combine(_root, "library"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private AudiobookManagerSettings ValidSettings() => new()
    {
        AudiobookImportPath = Path.Combine(_root, "import"),
        AudiobookLibraryPath = Path.Combine(_root, "library"),
        DbLocation = Path.Combine(_root, "config", "abm.db"),
    };

    [TestMethod]
    public void EnsureRequiredPathsAreUsable_EverythingPresent_DoesNotThrow()
    {
        SettingsValidation.EnsureRequiredPathsAreUsable(ValidSettings());
    }

    // The database file itself is created by SQLite on first run, so only its directory has to
    // exist - requiring the file would fail every fresh install.
    [TestMethod]
    public void EnsureRequiredPathsAreUsable_DatabaseFileDoesNotExistYet_DoesNotThrow()
    {
        var settings = ValidSettings();

        Assert.IsFalse(File.Exists(settings.DbLocation));
        SettingsValidation.EnsureRequiredPathsAreUsable(settings);
    }

    [TestMethod]
    public void EnsureRequiredPathsAreUsable_BareDatabaseFileName_DoesNotThrow()
    {
        var settings = ValidSettings();
        settings.DbLocation = "audiobookmanager.db";

        SettingsValidation.EnsureRequiredPathsAreUsable(settings);
    }

    [TestMethod]
    public void EnsureRequiredPathsAreUsable_MissingImportDirectory_ThrowsNamingTheSettingAndValue()
    {
        var settings = ValidSettings();
        settings.AudiobookImportPath = Path.Combine(_root, "not-mounted");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => SettingsValidation.EnsureRequiredPathsAreUsable(settings));

        StringAssert.Contains(ex.Message, nameof(AudiobookManagerSettings.AudiobookImportPath));
        StringAssert.Contains(ex.Message, Path.Combine(_root, "not-mounted"));
    }

    [TestMethod]
    public void EnsureRequiredPathsAreUsable_MissingLibraryDirectory_Throws()
    {
        var settings = ValidSettings();
        settings.AudiobookLibraryPath = Path.Combine(_root, "not-mounted");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => SettingsValidation.EnsureRequiredPathsAreUsable(settings));

        StringAssert.Contains(ex.Message, nameof(AudiobookManagerSettings.AudiobookLibraryPath));
    }

    [TestMethod]
    public void EnsureRequiredPathsAreUsable_DatabaseDirectoryMissing_Throws()
    {
        var settings = ValidSettings();
        settings.DbLocation = Path.Combine(_root, "no-such-config", "abm.db");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => SettingsValidation.EnsureRequiredPathsAreUsable(settings));

        StringAssert.Contains(ex.Message, nameof(AudiobookManagerSettings.DbLocation));
    }

    [TestMethod]
    public void EnsureRequiredPathsAreUsable_UnsetPaths_AreReportedAsNotSet()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => SettingsValidation.EnsureRequiredPathsAreUsable(new AudiobookManagerSettings
            {
                AudiobookImportPath = string.Empty,
                AudiobookLibraryPath = "   ",
                DbLocation = Path.Combine(_root, "config", "abm.db"),
            }));

        StringAssert.Contains(ex.Message, $"{nameof(AudiobookManagerSettings.AudiobookImportPath)} is not set.");
        StringAssert.Contains(ex.Message, $"{nameof(AudiobookManagerSettings.AudiobookLibraryPath)} is not set.");
    }

    // One startup failure listing everything that is wrong, rather than fixing them one restart
    // at a time.
    [TestMethod]
    public void EnsureRequiredPathsAreUsable_SeveralProblems_ReportsAllOfThem()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => SettingsValidation.EnsureRequiredPathsAreUsable(new AudiobookManagerSettings
            {
                AudiobookImportPath = Path.Combine(_root, "missing-import"),
                AudiobookLibraryPath = Path.Combine(_root, "missing-library"),
                DbLocation = Path.Combine(_root, "missing-config", "abm.db"),
            }));

        StringAssert.Contains(ex.Message, "missing-import");
        StringAssert.Contains(ex.Message, "missing-library");
        StringAssert.Contains(ex.Message, "missing-config");
    }

    // A file where a directory is expected is a real misconfiguration (mounting the library at a
    // file path), and Directory.Exists is false for it - but the message must say so.
    [TestMethod]
    public void EnsureRequiredPathsAreUsable_LibraryPathPointsAtAFile_Throws()
    {
        var filePath = Path.Combine(_root, "library-is-a-file");
        File.WriteAllText(filePath, "not a directory");

        var settings = ValidSettings();
        settings.AudiobookLibraryPath = filePath;

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => SettingsValidation.EnsureRequiredPathsAreUsable(settings));

        StringAssert.Contains(ex.Message, "is not a directory");
    }
}
