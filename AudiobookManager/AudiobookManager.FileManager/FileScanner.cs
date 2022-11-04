using AudiobookManager.Domain;

namespace AudiobookManager.FileManager;
public class FileScanner
{
    public static List<Audiobook> ParseDirectoryForAudiobooks(string path)
    {
        var result = new List<Audiobook>();

        foreach (string sPath in Directory.GetFiles(path))
        {
            try
            {
                var book = AudiobookParser.ParseAudiobook(sPath);
                result.Add(book);
            }
            catch
            {

            }
        }

        foreach (string sPath in Directory.GetDirectories(path))
        {
            result.AddRange(ParseDirectoryForAudiobooks(sPath));
        }

        return result;
    }
}
