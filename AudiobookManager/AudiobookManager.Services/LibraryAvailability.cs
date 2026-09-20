using AudiobookManager.Settings;

namespace AudiobookManager.Services;

/// <summary>
/// The single "is the library on disk actually usable right now" guard, shared by every caller
/// whose behaviour on a missing library is destructive rather than merely broken.
///
/// Startup validation (<see cref="SettingsValidation.EnsureRequiredPathsAreUsable"/>) answers
/// this once; a volume mount can disappear afterwards, and the combined scan and the consistency
/// check both wipe per-run tables before their work starts, so they have to re-ask at the point
/// of use. Keeping the guard (and the exception message) in one place stops the refusals from
/// drifting apart.
/// </summary>
public static class LibraryAvailability
{
    /// <summary>
    /// Whether the configured library directory is present and usable right now.
    /// </summary>
    public static bool IsUsable(AudiobookManagerSettings settings) =>
        SettingsValidation.IsDirectoryUsable(settings.AudiobookLibraryPath);

    /// <summary>
    /// The message every refusal shows. Split out so a controller can refuse synchronously (the
    /// fire-and-forget path would otherwise report a missing mount as a zeroed completion event)
    /// with exactly the wording the throwing guard produces.
    /// </summary>
    public static string UnavailableMessage(AudiobookManagerSettings settings) =>
        $"The library directory '{settings.AudiobookLibraryPath}' is not available, so every book would "
        + "look missing. This is normally a volume mount - check it is mounted and readable by the user "
        + "this application runs as, then run the check again.";

    /// <summary>
    /// Throws <see cref="LibraryUnavailableException"/> when the library directory is not usable.
    /// Nothing is cleared or written before this is called: on a missing mount every book's
    /// <c>File.Exists</c> is false, so running would record the whole library as missing - which
    /// the bulk resolve then turns into deleting every record.
    /// </summary>
    public static void EnsureUsable(AudiobookManagerSettings settings)
    {
        if (IsUsable(settings))
        {
            return;
        }

        throw new LibraryUnavailableException(UnavailableMessage(settings));
    }
}
