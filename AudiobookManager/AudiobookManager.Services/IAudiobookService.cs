using AudiobookManager.Domain;

namespace AudiobookManager.Services;
public interface IAudiobookService
{
    Audiobook ParseAudiobook(string filePath);

    Audiobook OrganizeAudiobook(Audiobook audiobook);
}
