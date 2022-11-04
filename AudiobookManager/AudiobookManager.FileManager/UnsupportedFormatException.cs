namespace AudiobookManager.FileManager
{
    public class UnsupportedFormatException : Exception
    {
        public UnsupportedFormatException() : base()
        {
        }

        public UnsupportedFormatException(string? message) : base(message)
        {
        }

        public UnsupportedFormatException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
