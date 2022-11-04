namespace AudiobookManager.Domain;

public class AudiobookImage
{
    public string Base64Data { get; set; }
    public string MimeType { get; set; }

    public AudiobookImage(string base64Data, string mimeType)
    {
        Base64Data = base64Data;
        MimeType = mimeType;
    }
}
