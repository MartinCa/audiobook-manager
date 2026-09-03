namespace AudiobookManager.Services;

/// <summary>
/// Thrown when an operation refuses to run because the library on disk does not look like the
/// library the database describes, so acting on the difference would destroy rather than repair.
///
/// The two cases this covers are both ordinary operational events, not attacks: the library path
/// is a volume mount in the normal deployment, and a mount that has gone away (or come back
/// empty) makes every book look deleted. <see cref="SettingsValidation"/> checks these paths at
/// startup, but only once - by the time a consistency run or a bulk resolve happens the answer
/// can have changed.
/// </summary>
public class LibraryUnavailableException : Exception
{
    public LibraryUnavailableException(string message) : base(message)
    {
    }
}
