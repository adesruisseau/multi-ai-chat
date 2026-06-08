using AgentGroupChat.Core.Services;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Hybrid.State;
using AgentGroupChat.Infrastructure.Data;
using AgentGroupChat.Infrastructure.Migration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace AgentGroupChat.Hybrid;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();

        // Database
        //C:\Users\...\AppData\Local\AgentGroupChat\agentgroupchat.db
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

		// Legacy migration
		builder.Services.AddScoped<ILegacyDataSource, LegacyJsonDataSource>();
		builder.Services.AddScoped<DataMigrator>();

		// Core services
		builder.Services.AddSingleton<HttpClient>();
		builder.Services.AddSingleton<LlmClient>();
		builder.Services.AddSingleton<ImageClient>();
		builder.Services.AddSingleton<SpeechService>(sp => new SpeechService(sp.GetRequiredService<HttpClient>()));
		builder.Services.AddScoped<PromptComposer>();
		builder.Services.AddScoped<TurnExecutor>();
		builder.Services.AddScoped<MemorySummarizer>();
		builder.Services.AddScoped<ConversationRunner>();
		builder.Services.AddScoped<SceneRetrievalService>();
		builder.Services.AddScoped<LogService>();

		// State containers
		builder.Services.AddScoped<AppState>();
		builder.Services.AddScoped<RoomState>();
		builder.Services.AddScoped<PromptLibraryState>();
		builder.Services.AddScoped<ConversationState>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Run migration + DB creation on startup
		using (var scope = app.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			db.Database.EnsureCreated();

			var migrator = scope.ServiceProvider.GetRequiredService<DataMigrator>();
			migrator.MigrateIfNeededAsync().GetAwaiter().GetResult();
		}

		return app;
	}
}
