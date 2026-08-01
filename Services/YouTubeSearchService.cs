using System.Text.Json;

namespace MovieSync.Web.Services
{
    public class YouTubeSearchResult
    {
        public string VideoId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string PublishedTime { get; set; } = string.Empty;
        public string ViewCount { get; set; } = string.Empty;
    }

    public class YouTubeSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public YouTubeSearchService(IConfiguration config)
        {
            _httpClient = new HttpClient();
            _apiKey = config["YouTube:ApiKey"] ?? string.Empty;
        }

        public async Task<List<YouTubeSearchResult>> SearchAsync(string query)
        {
            var results = new List<YouTubeSearchResult>();

            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrEmpty(_apiKey))
            {
                return results;
            }

            try
            {
                string searchUrl = "https://www.googleapis.com/youtube/v3/search" +
                    $"?part=snippet&type=video&maxResults=12&q={Uri.EscapeDataString(query)}&key={_apiKey}";

                var searchResp = await _httpClient.GetStringAsync(searchUrl);
                using var searchDoc = JsonDocument.Parse(searchResp);

                var videoIds = new List<string>();
                var basicInfo = new Dictionary<string, (string Title, string Channel, string Thumb)>();

                foreach (var item in searchDoc.RootElement.GetProperty("items").EnumerateArray())
                {
                    var videoId = item.GetProperty("id").GetProperty("videoId").GetString();
                    if (string.IsNullOrEmpty(videoId)) continue;

                    var snippet = item.GetProperty("snippet");
                    var title = snippet.GetProperty("title").GetString() ?? "";
                    var channel = snippet.GetProperty("channelTitle").GetString() ?? "";
                    var thumb = snippet.GetProperty("thumbnails").GetProperty("medium").GetProperty("url").GetString() ?? "";

                    videoIds.Add(videoId);
                    basicInfo[videoId] = (title, channel, thumb);
                }

                if (videoIds.Count == 0) return results;

                // Second call to get durations/view counts (search endpoint doesn't return these)
                string detailsUrl = "https://www.googleapis.com/youtube/v3/videos" +
                    $"?part=contentDetails,statistics&id={string.Join(",", videoIds)}&key={_apiKey}";

                var detailsResp = await _httpClient.GetStringAsync(detailsUrl);
                using var detailsDoc = JsonDocument.Parse(detailsResp);

                foreach (var item in detailsDoc.RootElement.GetProperty("items").EnumerateArray())
                {
                    var videoId = item.GetProperty("id").GetString() ?? "";
                    if (!basicInfo.TryGetValue(videoId, out var info)) continue;

                    string duration = "";
                    if (item.TryGetProperty("contentDetails", out var cd) &&
                        cd.TryGetProperty("duration", out var durProp))
                    {
                        duration = FormatIsoDuration(durProp.GetString() ?? "");
                    }

                    string views = "";
                    if (item.TryGetProperty("statistics", out var stats) &&
                        stats.TryGetProperty("viewCount", out var viewProp))
                    {
                        views = viewProp.GetString() ?? "";
                    }

                    results.Add(new YouTubeSearchResult
                    {
                        VideoId = videoId,
                        Title = info.Title,
                        ChannelName = info.Channel,
                        ThumbnailUrl = info.Thumb,
                        Duration = duration,
                        ViewCount = views
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"YouTube search failed: {ex.Message}");
            }

            return results;
        }

        private string FormatIsoDuration(string iso)
        {
            // Parses PT#H#M#S into H:MM:SS or M:SS
            try
            {
                var span = System.Xml.XmlConvert.ToTimeSpan(iso);
                return span.Hours > 0
                    ? $"{span.Hours}:{span.Minutes:D2}:{span.Seconds:D2}"
                    : $"{span.Minutes}:{span.Seconds:D2}";
            }
            catch
            {
                return "";
            }
        }
    }
}