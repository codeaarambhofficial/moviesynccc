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

// Initialize Firebase Admin SDK
try
{
    string projectId = "moviesync-9bcef";
    var configPath = Path.Combine(builder.Environment.ContentRootPath, "firebase-applet-config.json");
    if (File.Exists(configPath))
    {
        var configJson = File.ReadAllText(configPath);
        using var doc = System.Text.Json.JsonDocument.Parse(configJson);
        if (doc.RootElement.TryGetProperty("projectId", out var projProp) && !string.IsNullOrEmpty(projProp.GetString()))
        {
            projectId = projProp.GetString()!;
        }
    }

    GoogleCredential? firebaseCredential = null;
    var firebaseJsonEnv = Environment.GetEnvironmentVariable("FIREBASE_CREDENTIALS_JSON");

    if (!string.IsNullOrEmpty(firebaseJsonEnv))
    {
        try
        {
            firebaseCredential = GoogleCredential.FromJson(firebaseJsonEnv);
        }
        catch (Exception jsonEx)
        {
            Console.WriteLine($"Could not parse FIREBASE_CREDENTIALS_JSON: {jsonEx.Message}");
        }
    }
    else
    {
        var accountKeyPath = Path.Combine(builder.Environment.ContentRootPath, "firebase-service-account.json");
        if (File.Exists(accountKeyPath))
        {
            firebaseCredential = GoogleCredential.FromFile(accountKeyPath);
        }
    }

    if (FirebaseApp.DefaultInstance == null)
    {
        var appOptions = new AppOptions
        {
            ProjectId = projectId
        };
        if (firebaseCredential != null)
        {
            appOptions.Credential = firebaseCredential;
        }

        FirebaseApp.Create(appOptions);
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