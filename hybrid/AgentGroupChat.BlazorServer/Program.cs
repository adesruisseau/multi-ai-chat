using AgentGroupChat.Core.Services;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Data;
using AgentGroupChat.Infrastructure.Identity;
using AgentGroupChat.Infrastructure.LegacyMigrations;
using AgentGroupChat.Infrastructure.Seeding;
using AgentGroupChat.UI.Shared.State;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using System.Data;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// SERVICES
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();

// Database
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AgentGroupChat", "agentgroupchat.db");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Repositories
builder.Services.AddScoped<IRoomRepository, RoomRepository>();
builder.Services.AddScoped<ISettingsRepository, SettingsRepository>();
builder.Services.AddScoped<IPromptSampleRepository, PromptSampleRepository>();
builder.Services.AddScoped<ITranscriptRepository, TranscriptRepository>();
builder.Services.AddScoped<IMemoryRepository, MemoryRepository>();
builder.Services.AddScoped<ISceneArchiveRepository, SceneArchiveRepository>();
builder.Services.AddScoped<ILogRepository, LogRepository>();
builder.Services.AddScoped<IDataSeeder, DataSeeder>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserContext, UserContext>();
// Legacy migration
//builder.Services.AddScoped<ILegacyDataSource, LegacyJsonDataSource>();
//builder.Services.AddScoped<DataMigrator>();

// Core services
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<LlmClient>();
builder.Services.AddSingleton<ImageClient>();
builder.Services.AddSingleton<SpeechService>(sp =>
    new SpeechService(sp.GetRequiredService<HttpClient>()));

builder.Services.AddScoped<PromptComposer>();
builder.Services.AddScoped<TurnExecutor>();
builder.Services.AddScoped<MemorySummarizer>();
builder.Services.AddScoped<ConversationRunner>();
builder.Services.AddScoped<SceneRetrievalService>();
builder.Services.AddScoped<LogService>();

// State
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<RoomState>();
builder.Services.AddScoped<PromptLibraryState>();
builder.Services.AddScoped<ConversationState>();

// Identity
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Logging + auth (MUST be before Build)
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Logging.AddDebug();

// BUILD APP
var app = builder.Build();

app.MapAuthEndpoints();

// MIGRATION 
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<IDataSeeder>();
    await seeder.SeedAsync();
}


// PIPELINE
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Cookie issuance must happen on a real HTTP response, not over the Blazor circuit.
        app.MapPost("/auth/login", async (
            HttpContext httpContext,
            [FromForm] LoginModel req,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var returnUrl = await GetReturnUrlAsync(httpContext);
            var result = await signInManager.PasswordSignInAsync(
                req.UserName,
                req.Password,
                false,
                false);

            return result.Succeeded
                ? Results.Redirect(returnUrl)
                : Results.Redirect(BuildLoginUrl("Login failed.", returnUrl));
        });

        app.MapPost("/auth/register", async (
            HttpContext httpContext,
            [FromForm] RegisterModel req,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var returnUrl = await GetReturnUrlAsync(httpContext);

            if (!string.Equals(req.Password, req.ConfirmPassword, StringComparison.Ordinal))
            {
                return Results.Redirect(BuildLoginUrl("Passwords do not match.", returnUrl));
            }

            var user = new ApplicationUser
            {
                UserName = req.UserName,
                Email = req.Email
            };

            var result = await userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
            {
                var message = string.Join(" ", result.Errors.Select(static error => error.Description));
                return Results.Redirect(BuildLoginUrl(message, returnUrl));
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            return Results.Redirect(returnUrl);
        });
    }

    private static async Task<string> GetReturnUrlAsync(HttpContext httpContext)
    {
        var form = await httpContext.Request.ReadFormAsync();
        return NormalizeReturnUrl(form["returnUrl"]);
    }

    private static string BuildLoginUrl(string error, string returnUrl)
    {
        var query = $"error={Uri.EscapeDataString(string.IsNullOrWhiteSpace(error) ? "Authentication failed." : error)}";
        if (returnUrl != "/")
        {
            query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        return $"/login?{query}";
    }

    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        returnUrl = returnUrl.Trim();
        if (!returnUrl.StartsWith("/", StringComparison.Ordinal))
        {
            return "/";
        }

        if (returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }
}