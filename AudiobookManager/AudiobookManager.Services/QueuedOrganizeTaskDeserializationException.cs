namespace AudiobookManager.Services;

/// <summary>
/// A queued organize task's <c>json_audiobook</c> column could not be deserialised back into an
/// <c>Audiobook</c>.
///
/// The most likely cause is a breaking change to the <c>Audiobook</c> shape landing between when
/// the row was queued and when it was picked up - a renamed property, a type change, a removed
/// member with a <c>required</c> replacement - which makes every row queued before the upgrade
/// undeserialisable after it. Ordinary corruption is possible too. Either way the row is not
/// deleted here: its JSON may be the only surviving copy of edits the user made before queuing.
/// <see cref="IQueuedOrganizeTaskRepository.RecordDeserializationFailureAsync"/> tracks the
/// failure on the row itself so a repeatedly-failing row eventually drops out of
/// <c>GetNextQueuedOrganizeTask</c> instead of blocking every row behind it (see #1322).
/// </summary>
public class QueuedOrganizeTaskDeserializationException : Exception
{
    public QueuedOrganizeTaskDeserializationException(string originalFileLocation, Exception innerException)
        : base($"Failed to deserialize audiobook JSON for queued task at '{originalFileLocation}'", innerException)
    {
        OriginalFileLocation = originalFileLocation;
    }

    public string OriginalFileLocation { get; }
}
