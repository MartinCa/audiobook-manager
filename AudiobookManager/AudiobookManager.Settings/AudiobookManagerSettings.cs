namespace AudiobookManager.Settings;
public class AudiobookManagerSettings
{
    public string AudiobookImportPath { get; set; }
    public string AudiobookLibraryPath { get; set; }
    public string DbLocation { get; set; } = "/config/audiobookmanager.db";
}
