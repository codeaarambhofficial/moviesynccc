using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace MovieSync.Web.Controllers
{
    [ApiController]
    [Route("api/stream")]
    public class StreamingController : ControllerBase
    {
        private static readonly Dictionary<string, string> ActiveStreams = new();

        [HttpPost("host")]
        public IActionResult HostStream([FromBody] HostStreamRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.FilePath)) return BadRequest("File path is required.");
            if (!System.IO.File.Exists(req.FilePath)) return NotFound($"File not found: {req.FilePath}");

            // Generate a unique stream ID for this file
            var streamId = Guid.NewGuid().ToString("N");
            ActiveStreams[streamId] = req.FilePath;

            return Ok(new { streamUrl = $"/api/stream/play/{streamId}" });
        }

        [HttpGet("play/{streamId}")]
        public IActionResult PlayStream(string streamId)
        {
            if (!ActiveStreams.TryGetValue(streamId, out var filePath)) return NotFound("Stream not found.");
            if (!System.IO.File.Exists(filePath)) return NotFound("File no longer exists.");

            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var mimeType = "video/mp4";
            if (filePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) mimeType = "video/x-matroska";
            else if (filePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)) mimeType = "video/webm";

            return File(fileStream, mimeType, enableRangeProcessing: true);
        }
    }

    public class HostStreamRequest
    {
        public string FilePath { get; set; } = "";
    }
}
