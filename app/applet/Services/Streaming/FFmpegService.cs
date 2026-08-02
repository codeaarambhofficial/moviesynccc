using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MovieSync.Web.Services.Streaming
{
    public interface IFFmpegService
    {
        Task<MediaMetadata> GetMediaMetadataAsync(string filePath);
        Task GenerateHlsStreamAsync(string inputFilePath, string outputDir, Action<bool> onFirstSegmentReady, CancellationToken cancellationToken);
    }

    public class FFmpegService : IFFmpegService
    {
        public async Task<MediaMetadata> GetMediaMetadataAsync(string filePath)
        {
            var metadata = new MediaMetadata();
            if (!File.Exists(filePath)) return metadata;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string jsonOutput = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    using var doc = JsonDocument.Parse(jsonOutput);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("format", out var formatProp))
                    {
                        if (formatProp.TryGetProperty("duration", out var durProp) && double.TryParse(durProp.GetString(), out double dur))
                        {
                            metadata.DurationSeconds = dur;
                        }
                        if (formatProp.TryGetProperty("bit_rate", out var brProp) && long.TryParse(brProp.GetString(), out long br))
                        {
                            metadata.Bitrate = br;
                        }
                    }

                    if (root.TryGetProperty("streams", out var streamsProp) && streamsProp.ValueKind == JsonValueKind.Array)
                    {
                        int audioIdx = 0, subIdx = 0;
                        foreach (var stream in streamsProp.EnumerateArray())
                        {
                            string codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? "" : "";
                            string codecName = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";

                            if (codecType == "video" && string.IsNullOrEmpty(metadata.VideoCodec))
                            {
                                metadata.VideoCodec = codecName.ToUpperInvariant();
                                int width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 1920;
                                int height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 1080;
                                metadata.Resolution = $"{width}x{height}";
                            }
                            else if (codecType == "audio")
                            {
                                if (string.IsNullOrEmpty(metadata.AudioCodec)) metadata.AudioCodec = codecName.ToUpperInvariant();
                                string lang = "und";
                                if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var langProp))
                                {
                                    lang = langProp.GetString() ?? "und";
                                }
                                metadata.AudioTracks.Add(new TrackInfo
                                {
                                    Index = audioIdx++,
                                    Codec = codecName,
                                    Language = lang,
                                    Title = $"Audio Track {audioIdx} ({lang})"
                                });
                            }
                            else if (codecType == "subtitle")
                            {
                                string lang = "und";
                                if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var langProp))
                                {
                                    lang = langProp.GetString() ?? "und";
                                }
                                metadata.SubtitleTracks.Add(new TrackInfo
                                {
                                    Index = subIdx++,
                                    Codec = codecName,
                                    Language = lang,
                                    Title = $"Subtitle {subIdx} ({lang})"
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FFmpegService] Error parsing metadata for {filePath}: {ex.Message}");
            }

            return metadata;
        }

        public async Task GenerateHlsStreamAsync(string inputFilePath, string outputDir, Action<bool> onFirstSegmentReady, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDir);
            var playlistPath = Path.Combine(outputDir, "playlist.m3u8");
            var segmentPattern = Path.Combine(outputDir, "segment_%03d.ts");

            var metadata = await GetMediaMetadataAsync(inputFilePath);
            bool isH264 = metadata.VideoCodec.Contains("264") || metadata.VideoCodec.Contains("H264");
            bool isAac = metadata.AudioCodec.Contains("AAC");

            // If video is H264 and audio is AAC or streamable, copy codecs for instant transmuxing!
            string videoCodecArgs = isH264 ? "-c:v copy" : "-c:v libx264 -preset ultrafast -crf 23";
            string audioCodecArgs = isAac ? "-c:a copy" : "-c:a aac -b:a 128k";

            var arguments = $"-i \"{inputFilePath}\" {videoCodecArgs} {audioCodecArgs} -f hls -hls_time 4 -hls_list_size 0 -hls_segment_filename \"{segmentPattern}\" \"{playlistPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine($"[FFmpegService] Starting HLS generation: ffmpeg {arguments}");

            using var process = new Process { StartInfo = psi };
            process.Start();

            // Background poller to notify when first playlist segment is ready
            _ = Task.Run(async () =>
            {
                bool notified = false;
                while (!process.HasExited && !notified && !cancellationToken.IsCancellationRequested)
                {
                    if (File.Exists(playlistPath) && File.Exists(Path.Combine(outputDir, "segment_000.ts")))
                    {
                        notified = true;
                        onFirstSegmentReady?.Invoke(true);
                    }
                    await Task.Delay(500, cancellationToken);
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            if (File.Exists(playlistPath))
            {
                onFirstSegmentReady?.Invoke(true);
            }
            Console.WriteLine($"[FFmpegService] HLS generation completed for {inputFilePath}");
        }
    }
}
