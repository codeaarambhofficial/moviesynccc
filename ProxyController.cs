using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace MovieSync.Web.Controllers
{
    [Route("api")]
    [ApiController]
    public class ProxyController : ControllerBase
    {
        private readonly HttpClient _httpClient;

        public ProxyController()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false
            };
            _httpClient = new HttpClient(handler);
        }

        [HttpGet("proxy")]
        public async Task Proxy([FromQuery] string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("Missing 'url' parameter.");
                return;
            }

            if (!IsSafeUrl(url, out var err))
            {
                Response.StatusCode = 400;
                await Response.WriteAsync(err);
                return;
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (Request.Headers.TryGetValue("Range", out var range))
                {
                    request.Headers.TryAddWithoutValidation("Range", range.ToString());
                }

                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MovieSync/1.0");

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                int statusCode = (int)response.StatusCode;
                if (statusCode >= 300 && statusCode <= 399)
                {
                    var redirectUrl = response.Headers.Location?.ToString();
                    if (!string.IsNullOrEmpty(redirectUrl))
                    {
                        if (!Uri.TryCreate(redirectUrl, UriKind.Absolute, out _))
                        {
                            redirectUrl = new Uri(new Uri(url), redirectUrl).ToString();
                        }
                        Response.Redirect($"/api/proxy?url={Uri.EscapeDataString(redirectUrl)}");
                        return;
                    }
                }

                Response.StatusCode = statusCode;
                foreach (var header in response.Headers)
                {
                    if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                    Response.Headers[header.Key] = header.Value.ToArray();
                }

                foreach (var header in response.Content.Headers)
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }

                Response.Headers["Access-Control-Allow-Origin"] = "*";
                Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
                Response.Headers["Access-Control-Allow-Headers"] = "*";

                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                bool isM3u8 = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || 
                              contentType.Contains("x-mpegurl", StringComparison.OrdinalIgnoreCase) || 
                              url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

                if (isM3u8)
                {
                    var manifestContent = await response.Content.ReadAsStringAsync();
                    var rewritten = RewriteM3u8Manifest(manifestContent, url);
                    Response.ContentType = "application/vnd.apple.mpegurl";
                    await Response.WriteAsync(rewritten);
                }
                else
                {
                    using var responseStream = await response.Content.ReadAsStreamAsync();
                    await responseStream.CopyToAsync(Response.Body);
                }
            }
            catch (Exception ex)
            {
                if (!Response.HasStarted)
                {
                    Response.StatusCode = 500;
                    await Response.WriteAsync($"Proxy error: {ex.Message}");
                }
            }
        }

        [HttpOptions("proxy")]
        public IActionResult ProxyOptions()
        {
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            Response.Headers["Access-Control-Allow-Headers"] = "*";
            return Ok();
        }

        private bool IsSafeUrl(string url, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                errorMessage = "Invalid URL format.";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                errorMessage = "Only HTTP and HTTPS schemes are allowed.";
                return false;
            }

            var host = uri.Host;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("127.0.0.1") || host.Equals("::1"))
            {
                errorMessage = "SSRF warning: localhost or loopback access is forbidden.";
                return false;
            }

            if (System.Net.IPAddress.TryParse(host, out var ip))
            {
                if (System.Net.IPAddress.IsLoopback(ip) || IsPrivateIP(ip))
                {
                    errorMessage = "SSRF warning: Private network access is forbidden.";
                    return false;
                }
            }

            return true;
        }

        private bool IsPrivateIP(System.Net.IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }
            return false;
        }

        private string RewriteM3u8Manifest(string manifestContent, string baseUrl)
        {
            var lines = manifestContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (!line.StartsWith("#"))
                {
                    string absoluteUrl = ResolveUri(baseUrl, line);
                    lines[i] = $"/api/proxy?url={Uri.EscapeDataString(absoluteUrl)}";
                }
                else if (line.StartsWith("#EXT-X-STREAM-INF") || line.StartsWith("#EXT-X-MEDIA") || line.StartsWith("#EXT-X-KEY"))
                {
                    lines[i] = Regex.Replace(line, @"URI=""([^""]+)""", m =>
                    {
                        string relativeUri = m.Groups[1].Value;
                        string absoluteUrl = ResolveUri(baseUrl, relativeUri);
                        return $@"URI=""/api/proxy?url={Uri.EscapeDataString(absoluteUrl)}""";
                    });
                }
            }
            return string.Join("\n", lines);
        }

        private string ResolveUri(string baseUrl, string relativeUrl)
        {
            if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out _))
            {
                return relativeUrl;
            }
            var baseUri = new Uri(baseUrl);
            var resolvedUri = new Uri(baseUri, relativeUrl);
            return resolvedUri.ToString();
        }
    }
}
