using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using MovieSync.Web.Hubs;
using MovieSync.Web.Services;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables (.env) — only if the file exists (local dev only)
var envPath = Path.Combine(builder.Environment.ContentRootPath, ".env");
if (File.Exists(envPath))
{
    DotNetEnv.Env.Load(envPath);
}
builder.Configuration.AddEnvironmentVariables();

// Initialize Firebase Admin SDK — supports either a local file (dev) 
// or a JSON string from an environment variable (production/Render)
try
{
    GoogleCredential? firebaseCredential = null;
    var firebaseJsonEnv = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_JSON");

    if (!string.IsNullOrEmpty(firebaseJsonEnv))
    {
        firebaseCredential = GoogleCredential.FromAccessToken(firebaseJsonEnv); // or json
    }
    else
    {
        var accountKeyPath = Path.Combine(builder.Environment.ContentRootPath, "firebase-service-account.json");
        if (File.Exists(accountKeyPath))
        {
            firebaseCredential = GoogleCredential.FromFile(accountKeyPath);
        }
    }

    if (firebaseCredential != null && FirebaseApp.DefaultInstance == null)
    {
        FirebaseApp.Create(new AppOptions()
        {
            Credential = firebaseCredential
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Firebase Admin SDK initialization skipped or warning: {ex.Message}");
}

// Register Default Authentication Scheme and Cookie Options
builder.Services.AddAuthentication("Identity.Application")
    .AddCookie("Identity.Application", options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/api/auth/logout";
        options.AccessDeniedPath = "/Login";

        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;

        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Razor Pages
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Login", "");
});

// API Controllers
builder.Services.AddControllers();

// SignalR
builder.Services.AddSignalR();

// Application Services
builder.Services.AddSingleton<RoomStateManager>();
builder.Services.AddSingleton<YouTubeSearchService>();

var app = builder.Build();

// Error Handling
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Razor Pages
app.MapRazorPages();

// API Controllers
app.MapControllers();

// SignalR Hub
app.MapHub<MovieSyncHub>("/hubs/moviesync");

app.Run();