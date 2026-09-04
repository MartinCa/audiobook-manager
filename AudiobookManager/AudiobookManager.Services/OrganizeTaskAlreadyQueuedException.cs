namespace AudiobookManager.Services;

/// <summary>
/// The same source file has already been queued for organizing.
///
/// <c>QueuedOrganizeTask</c>'s primary key is the original file path, so a second queue of the
/// same file violates it. That surfaced as a <c>DbUpdateException</c> out of the repository and,
/// with no exception handler registered, as an empty 500 - the user clicked Organize twice and was
/// shown nothing at all. It is not a server fault: the first request did exactly what was asked.
/// </summary>
public class OrganizeTaskAlreadyQueuedException : Exception
{
    public OrganizeTaskAlreadyQueuedException(string originalFileLocation)
        : base($"'{originalFileLocation}' is already queued for organizing.")
    {
        OriginalFileLocation = originalFileLocation;
    }

    public string OriginalFileLocation { get; }
}
