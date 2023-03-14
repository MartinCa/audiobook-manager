using System.Text.Json;
using AudiobookManager.Domain;

namespace AudiobookManager.Services.MappingExtensions;
public static class QueuedOrganizeTaskMapping
{
    public static QueuedOrganizeTask ToDomain(this Database.Models.QueuedOrganizeTask dbEntity) => new QueuedOrganizeTask(dbEntity.OriginalFileLocation, JsonSerializer.Deserialize<Audiobook>(dbEntity.JsonAudiobook), dbEntity.QueuedTime);

    public static Database.Models.QueuedOrganizeTask ToDb(this QueuedOrganizeTask domainModel) => new Database.Models.QueuedOrganizeTask(domainModel.OriginalFileLocation, JsonSerializer.Serialize(domainModel.Audiobook), domainModel.QueuedTime ?? DateTime.UtcNow);
}
