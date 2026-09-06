using System.Text.Json;
using AudiobookManager.Domain;

namespace AudiobookManager.Services.MappingExtensions;
public static class QueuedOrganizeTaskMapping
{
    private sealed record QueuedAudiobookPayload(Audiobook? Audiobook, bool MetadataAppliedFromSearch);

    public static QueuedOrganizeTask ToDomain(this Database.Models.QueuedOrganizeTask dbEntity)
    {
        var payload = JsonSerializer.Deserialize<QueuedAudiobookPayload>(dbEntity.JsonAudiobook);
        if (payload?.Audiobook is not null)
        {
            return new QueuedOrganizeTask(
                dbEntity.OriginalFileLocation,
                payload.Audiobook,
                dbEntity.QueuedTime,
                payload.MetadataAppliedFromSearch);
        }

        // Rows queued before the envelope was introduced contain the audiobook object directly.
        // They remain valid ordinary organizes and, by definition, carry no refresh signal.
        var audiobook = JsonSerializer.Deserialize<Audiobook>(dbEntity.JsonAudiobook)
            ?? throw new InvalidOperationException($"Failed to deserialize audiobook JSON for queued task at '{dbEntity.OriginalFileLocation}'");
        return new QueuedOrganizeTask(dbEntity.OriginalFileLocation, audiobook, dbEntity.QueuedTime);
    }

    public static Database.Models.QueuedOrganizeTask ToDb(this QueuedOrganizeTask domainModel) =>
        new Database.Models.QueuedOrganizeTask(
            domainModel.OriginalFileLocation,
            JsonSerializer.Serialize(new QueuedAudiobookPayload(domainModel.Audiobook, domainModel.MetadataAppliedFromSearch)),
            domainModel.QueuedTime ?? DateTime.UtcNow);
}
