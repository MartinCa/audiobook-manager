using ATL;
using ATL.AudioData;

namespace AudiobookManager.FileManager.Mediatypes
{
    public static class AudiotypeFactory
    {
        public static IMediafile GetMediafileFromTrack(Track track)
        {
            switch (track.AudioFormat.ID)
            {
                case AudioDataIOFactory.CID_MP4:
                    return new Mp4();
            }

            throw new UnsupportedFormatException();
        }
    }
}
