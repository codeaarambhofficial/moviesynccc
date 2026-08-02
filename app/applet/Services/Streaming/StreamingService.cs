using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MovieSync.Web.Services.Streaming
{
    public interface IStreamingService
    {
        Task<MediaStreamSession> RegisterLocalFilePathAsync(string filePath, string roomId);
        Task<MediaStreamSession> CreateChunkedStreamSessionAsync(string fileName, string roomId);
        Task<bool> AppendUploadChunkAsync(string streamId, byte[] chunkData, bool isLast);
        MediaStreamSession? GetStreamSession(string streamId);
        void CleanupSession(string streamId);
    }

    public class StreamingService : IStreamingService
    {
        private readonly IFFmpegService _ffmpegService;
        private readonly ConcurrentDictionary<string, MediaStreamSession> _activeSessions = new();
        private readonly string _baseStoragePath;

        public StreamingService(IFFmpegService ffmpegService)
        {
            _ffmpegService = ffmpegService;
            _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "media");
            Directory.CreateDirectory(Path.Combine(_baseStoragePath, "uploads"));
            Directory.CreateDirectory(Path.Combine(_baseStoragePath, "hls"));
        }

        public async Task<MediaStreamSession> RegisterLocalFilePathAsync(string filePath, string roomId)
        {
            string streamId = Guid.NewGuid().ToString("N");
            string hlsDir = Path.Combine(_baseStoragePath, "hls", streamId);
            string masterPlaylist = Path.Combine(hlsDir, "playlist.m3u8");

            var metadata = await _ffmpegService.GetMediaMetadataAsync(filePath);

            var session = new MediaStreamSession
            {
                StreamId = streamId,
                RoomId = roomId,
                OriginalFilePath = filePath,
                HlsDirectory = hlsDir,
                MasterPlaylistPath = masterPlaylist,
                Metadata = metadata,
                IsHlsReady = false,
                IsTranscoding = true
            };

            _activeSessions[streamId] = session;

            // Trigger background HLS generation
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ffmpegService.GenerateHlsStreamAsync(filePath, hlsDir, (ready) =>
                    {
                        session.IsHlsReady = ready;
                    }, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[StreamingService] HLS generation error: {ex.Message}");
                }
                finally
                {
                    session.IsTranscoding = false;
                }
            });

            return session;
        }

        public async Task<MediaStreamSession> CreateChunkedStreamSessionAsync(string fileName, string roomId)
        {
            string streamId = Guid.NewGuid().ToString("N");
            string uploadPath = Path.Combine(_baseStoragePath, "uploads", $"{streamId}_{fileName}");
            string hlsDir = Path.Combine(_baseStoragePath, "hls", streamId);
            string masterPlaylist = Path.Combine(hlsDir, "playlist.m3u8");

            // Ensure empty file is created
            using (var fs = File.Create(uploadPath)) { }

            var session = new MediaStreamSession
            {
                StreamId = streamId,
                RoomId = roomId,
                OriginalFilePath = uploadPath,
                HlsDirectory = hlsDir,
                MasterPlaylistPath = masterPlaylist,
                IsHlsReady = false,
                IsTranscoding = false
            };

            _activeSessions[streamId] = session;
            return await Task.FromResult(session);
        }

        public async Task<bool> AppendUploadChunkAsync(string streamId, byte[] chunkData, bool isLast)
        {
            if (!_activeSessions.TryGetValue(streamId, out var session))
            {
                return false;
            }

            try
            {
                using (var fs = new FileStream(session.OriginalFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    await fs.WriteAsync(chunkData, 0, chunkData.Length);
                }

                // Parse metadata once we have enough bytes or when last chunk is received
                if (session.Metadata.DurationSeconds == 0)
                {
                    session.Metadata = await _ffmpegService.GetMediaMetadataAsync(session.OriginalFilePath);
                }

                if (isLast && !session.IsTranscoding)
                {
                    session.IsTranscoding = true;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _ffmpegService.GenerateHlsStreamAsync(session.OriginalFilePath, session.HlsDirectory, (ready) =>
                            {
                                session.IsHlsReady = ready;
                            }, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[StreamingService] Chunked HLS transcode error: {ex.Message}");
                        }
                        finally
                        {
                            session.IsTranscoding = false;
                        }
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StreamingService] AppendUploadChunkAsync error: {ex.Message}");
                return false;
            }
        }

        public MediaStreamSession? GetStreamSession(string streamId)
        {
            _activeSessions.TryGetValue(streamId, out var session);
            return session;
        }

        public void CleanupSession(string streamId)
        {
            if (_activeSessions.TryRemove(streamId, out var session))
            {
                try
                {
                    if (Directory.Exists(session.HlsDirectory))
                    {
                        Directory.Delete(session.HlsDirectory, recursive: true);
                    }
                    if (File.Exists(session.OriginalFilePath) && session.OriginalFilePath.Contains("uploads"))
                    {
                        File.Delete(session.OriginalFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[StreamingService] Cleanup error for {streamId}: {ex.Message}");
                }
            }
        }
    }
}
