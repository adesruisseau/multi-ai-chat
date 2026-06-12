using AgentGroupChat.Core.Services;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Data;
using AgentGroupChat.Infrastructure.Identity;
using AgentGroupChat.Infrastructure.LegacyMigrations;
using AgentGroupChat.Infrastructure.Seeding;
using AgentGroupChat.UI.Shared.State;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using System.Data;

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
        app.MapPost("/auth/login", async (
            LoginModel req,
            SignInManager<ApplicationUser> signInManager) =>
        {
            var result = await signInManager.PasswordSignInAsync(
                req.UserName,
                req.Password,
                false,
                false);

            return result.Succeeded ? Results.Ok() : Results.Unauthorized();
        });
    }
}