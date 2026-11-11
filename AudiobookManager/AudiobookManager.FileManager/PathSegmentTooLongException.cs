namespace AudiobookManager.FileManager;

/// <summary>
/// A folder or file name generated from a book's metadata is longer than the file system allows
/// (<see cref="AudiobookFileHandler.MaxPathSegmentBytes"/>). It is the user's metadata that is too
/// long - a title or series, since the author folder is shortened rather than refused - so the
/// message is written to be shown: it carries the byte count and the start of the name, never an
/// absolute path.
///
/// An <see cref="InvalidOperationException"/> on purpose: the callers that already treat that type
/// as "the request cannot be carried out as given" (a single-book operation's failure message, the
/// metadata-refresh target check, the discovered-books duplicate check) keep doing so. Callers
/// that need to tell it apart from the library-root escape, which is the same type, catch this one.
/// </summary>
public sealed class PathSegmentTooLongException : InvalidOperationException
{
    public PathSegmentTooLongException(string message) : base(message)
    {
    }
}
