using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface ICoverImageProcessor
{
    /// <summary>
    /// Validates a client-supplied cover and returns it in a form worth storing.
    /// </summary>
    /// <param name="base64Data">The image bytes, base64-encoded, as the client sent them.</param>
    /// <param name="declaredMimeType">
    /// What the client says it is. Advisory only: the format is decided by looking at the bytes.
    /// </param>
    /// <exception cref="InvalidCoverImageException">
    /// The data is not an image this application can read, or is too large to decode.
    /// </exception>
    AudiobookImage Normalize(string base64Data, string? declaredMimeType);
}
