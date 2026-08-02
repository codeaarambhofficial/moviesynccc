using System.Collections.Generic;

namespace MovieSync.Web.Services.Streaming
{
    public class MediaMetadata
    {
        public double DurationSeconds { get; set; }
        public string VideoCodec { get; set; } = "H264";
        public string AudioCodec { get; set; } = "AAC";
        public string Resolution { get; set; } = "1920x1080";
        public long Bitrate { get; set; }
        public List<TrackInfo> AudioTracks { get; set; } = new();
        public List<TrackInfo> SubtitleTracks { get; set; } = new();
    }

    public class TrackInfo
    {
        public int Index { get; set; }
        public string Language { get; set; } = "und";
        public string Title { get; set; } = "";
        public string Codec { get; set; } = "";
    }

    public class MediaStreamSession
    {
        public string StreamId { get; set; } = string.Empty;
        public string RoomId { get; set; } = string.Empty;
        public string OriginalFilePath { get; set; } = string.Empty;
        public string HlsDirectory { get; set; } = string.Empty;
        public string MasterPlaylistPath { get; set; } = string.Empty;
        public bool IsHlsReady { get; set; }
        public bool IsTranscoding { get; set; }
        public int TranscodeProgress { get; set; }
        public MediaMetadata Metadata { get; set; } = new();
        public System.DateTime CreatedAt { get; set; } = System.DateTime.UtcNow;
    }
}
