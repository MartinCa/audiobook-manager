namespace AudiobookManager.Services;

/// <summary>
/// The cover a client supplied is not something this application will accept - not an image, an
/// image format it cannot read, or larger than it is willing to decode.
/// </summary>
public class InvalidCoverImageException : Exception
{
    public InvalidCoverImageException(string message) : base(message)
    {
    }
}
