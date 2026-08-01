using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MovieSync.Web.Pages
{
    [Authorize] // Forces users to log in via cookie before accessing the room
    public class RoomModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}