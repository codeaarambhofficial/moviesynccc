using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MovieSync.Web.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] FirebaseAuthDto model)
        {
            try
            {
                FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(model.IdToken);
                string uid = decodedToken.Uid;
                string email = decodedToken.Claims.TryGetValue("email", out var emailObj) ? emailObj?.ToString() ?? string.Empty : string.Empty;

                // Create the identity claims for the cookie
                var claims = new List<Claim> 
                { 
                    new Claim(ClaimTypes.Name, email), 
                    new Claim(ClaimTypes.NameIdentifier, uid) 
                };
                var claimsIdentity = new ClaimsIdentity(claims, "Identity.Application");

                // Sign the user in to issue the authentication cookie
                await HttpContext.SignInAsync("Identity.Application", new ClaimsPrincipal(claimsIdentity));

                return Ok(new { message = "Logged in successfully", uid, email });
            }
            catch (Exception ex)
            {
                return Unauthorized(new { message = "Invalid Firebase token", error = ex.Message });
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("Identity.Application");
            return Ok(new { message = "Logged out successfully" });
        }
    }

    public record FirebaseAuthDto(string IdToken);
}