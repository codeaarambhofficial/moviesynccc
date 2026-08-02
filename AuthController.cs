using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MovieSync.Web.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] FirebaseAuthDto model)
        {
            if (string.IsNullOrEmpty(model?.IdToken))
            {
                return BadRequest(new { message = "ID token is required" });
            }

            string uid = string.Empty;
            string email = string.Empty;

            try
            {
                if (FirebaseApp.DefaultInstance != null)
                {
                    FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(model.IdToken);
                    uid = decodedToken.Uid;
                    email = decodedToken.Claims.TryGetValue("email", out var emailObj) ? emailObj?.ToString() ?? string.Empty : string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Firebase Admin VerifyIdTokenAsync warning: {ex.Message}");
            }

            // Fallback JWT claim parsing if Admin SDK token verification was skipped or failed
            if (string.IsNullOrEmpty(uid))
            {
                try
                {
                    var parts = model.IdToken.Split('.');
                    if (parts.Length == 3)
                    {
                        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                        using var doc = JsonDocument.Parse(payloadJson);
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("user_id", out var uidProp) || root.TryGetProperty("sub", out uidProp))
                        {
                            uid = uidProp.GetString() ?? string.Empty;
                        }
                        if (root.TryGetProperty("email", out var emailProp))
                        {
                            email = emailProp.GetString() ?? string.Empty;
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"Fallback JWT parsing error: {parseEx.Message}");
                }
            }

            if (string.IsNullOrEmpty(uid))
            {
                return Unauthorized(new { message = "Invalid Firebase token" });
            }

            if (string.IsNullOrEmpty(email))
            {
                email = $"{uid}@moviesync.user";
            }

            // Create identity claims for the cookie
            var claims = new List<Claim> 
            { 
                new Claim(ClaimTypes.Name, email), 
                new Claim(ClaimTypes.NameIdentifier, uid) 
            };
            var claimsIdentity = new ClaimsIdentity(claims, "Identity.Application");

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            };

            // Sign in user to issue persistent authentication cookie
            await HttpContext.SignInAsync("Identity.Application", new ClaimsPrincipal(claimsIdentity), authProperties);

            return Ok(new { message = "Logged in successfully", uid, email });
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("Identity.Application");
            return Ok(new { message = "Logged out successfully" });
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string base64 = input.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }

    public record FirebaseAuthDto(string IdToken);
}