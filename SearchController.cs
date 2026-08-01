using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MovieSync.Web.Services;

namespace MovieSync.Web.Controllers
{
    [Route("api/search")]
    [ApiController]
    [Authorize] // Ensure search is only accessible by authenticated users
    public class SearchController : ControllerBase
    {
        private readonly YouTubeSearchService _searchService;

        public SearchController(YouTubeSearchService searchService)
        {
            _searchService = searchService;
        }

        [HttpGet("youtube")]
        public async Task<IActionResult> SearchYouTube([FromQuery] string query)
        {
            var results = await _searchService.SearchAsync(query);
            return Ok(results);
        }
    }
}
