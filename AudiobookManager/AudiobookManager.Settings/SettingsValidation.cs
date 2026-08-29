namespace AudiobookManager.Settings;

/// <summary>
/// Startup validation for the settings the application cannot run without.
/// </summary>
public static class SettingsValidation
{
    /// <summary>
    /// Verifies the import path, the library path and the database's directory are configured and
    /// actually present, throwing with everything that is wrong at once.
    ///
    /// These are the three paths the whole application is built on, and in the normal (container)
    /// deployment they are volume mounts - so a typo or a missing mount means nothing works. It
    /// used to fail later and per-feature instead: FileScanner throws DirectoryNotFoundException,
    /// so a bad import path 500'd the organize page and a bad library path 500'd the library scan,
    /// while other screens carried on looking healthy and the consistency check quietly reported
    /// no orphans because it guards with Directory.Exists. Failing at startup, naming the setting
    /// and the value, turns that into one obvious error.
    /// </summary>
    public static void EnsureRequiredPathsAreUsable(AudiobookManagerSettings settings)
    {
        var problems = new List<string>();

        CheckDirectory(problems, nameof(settings.AudiobookImportPath), settings.AudiobookImportPath);
        CheckDirectory(problems, nameof(settings.AudiobookLibraryPath), settings.AudiobookLibraryPath);
        CheckDatabaseLocation(problems, settings.DbLocation);

        if (problems.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Audiobook Manager cannot start because required paths are not usable:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => $"  - {p}"))
            + Environment.NewLine
            + "These are normally volume mounts; check they are mounted and readable by the user this "
            + "application runs as.");
    }

    private static void CheckDirectory(List<string> problems, string settingName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            problems.Add($"{settingName} is not set.");
            return;
        }

        if (!Directory.Exists(path))
        {
            problems.Add($"{settingName} '{path}' does not exist or is not a directory.");
        }
    }

    /// <summary>
    /// SQLite creates the database file but never its parent directory, so the directory is what
    /// has to exist - and a bare file name ("audiobookmanager.db") legitimately has none.
    /// </summary>
    private static void CheckDatabaseLocation(List<string> problems, string? dbLocation)
    {
        if (string.IsNullOrWhiteSpace(dbLocation))
        {
            problems.Add($"{nameof(AudiobookManagerSettings.DbLocation)} is not set.");
            return;
        }

        var directory = Path.GetDirectoryName(dbLocation);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            problems.Add(
                $"{nameof(AudiobookManagerSettings.DbLocation)} '{dbLocation}' is in a directory that does not exist "
                + $"('{directory}'). SQLite creates the database file but not its directory.");
        }
    }
}
