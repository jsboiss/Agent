using Agent.Compaction;
using Agent.Conversations;
using Agent.Dashboard;
using Agent.Endpoints;
using Agent.Events;
using Agent.Memory;
using Agent.Messages;
using Agent.Providers;
using Agent.Providers.ClaudeCode;
using Agent.Providers.Codex;
using Agent.Providers.Ollama;
using Agent.Resources;
using Agent.Settings;
using Agent.SubAgents;
using Agent.Tools;
using Agent.Workspaces;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.Configure<OllamaProviderOptions>(
    builder.Configuration.GetSection(OllamaProviderOptions.SectionName));
builder.Services.Configure<CodexProviderOptions>(
    builder.Configuration.GetSection("Providers:Codex"));
builder.Services.Configure<SqliteMemoryOptions>(
    builder.Configuration.GetSection("Memory:Sqlite"));
builder.Services.Configure<SqliteAgentStateOptions>(
    builder.Configuration.GetSection("Agent:Sqlite"));
builder.Services.AddHttpClient<OllamaProviderClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<OllamaProviderOptions>>().Value;
    OllamaProviderClient.ConfigureHttpClient(httpClient, options);
});
builder.Services.AddSingleton<IAgentProviderClient, ClaudeCodeProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient, CodexProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient>(x => x.GetRequiredService<OllamaProviderClient>());
builder.Services.AddSingleton<IAgentProviderSelector, AgentProviderSelector>();
builder.Services.AddSingleton<IConversationRepository, SqliteConversationRepository>();
builder.Services.AddSingleton<IConversationResolver, ConversationResolver>();
builder.Services.AddSingleton<IConversationSummaryStore, SqliteConversationSummaryStore>();
builder.Services.AddSingleton<IConversationCompactor, RollingConversationCompactor>();
builder.Services.AddSingleton<SqliteAgentStateStore>();
builder.Services.AddSingleton<IAgentWorkspaceStore>(x => x.GetRequiredService<SqliteAgentStateStore>());
builder.Services.AddSingleton<IAgentRunStore>(x => x.GetRequiredService<SqliteAgentStateStore>());
builder.Services.AddSingleton<IConversationMirrorStore>(x => x.GetRequiredService<SqliteAgentStateStore>());
builder.Services.AddSingleton<IAgentMessageRouter, AgentMessageRouter>();
builder.Services.AddSingleton<IAgentResourceLoader, AgentResourceLoader>();
builder.Services.AddSingleton<IConversationPromptQueue, InMemoryConversationPromptQueue>();
builder.Services.AddSingleton<IAgentSettingsResolver, ConfigurationAgentSettingsResolver>();
builder.Services.AddSingleton<ISubAgentWorkQueue, SubAgentWorkQueue>();
builder.Services.AddSingleton<ISubAgentCoordinator, SubAgentCoordinator>();
builder.Services.AddHostedService<SubAgentRunWorker>();
builder.Services.AddSingleton<IAgentEventStore, SqliteAgentEventStore>();
builder.Services.AddSingleton<IAgentEventSink>(x => x.GetRequiredService<IAgentEventStore>());
builder.Services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
builder.Services.AddSingleton<IMemoryScout, MemoryScout>();
builder.Services.AddSingleton<RuleBasedMemoryExtractor>();
builder.Services.AddSingleton<LlmMemoryExtractor>();
builder.Services.AddSingleton<IMemoryExtractor, CompositeMemoryExtractor>();
builder.Services.AddSingleton<IMemoryCandidateReviewer, MemoryCandidateReviewer>();
builder.Services.AddSingleton<IAgentToolExecutor, AgentToolExecutor>();
builder.Services.AddScoped<IChatDashboardService, ChatDashboardService>();
builder.Services.AddScoped<IMemoryDashboardService, MemoryDashboardService>();
builder.Services.AddScoped<IRunTimelineService, RunTimelineService>();
builder.Services.AddScoped<ISubAgentDashboardService, SubAgentDashboardService>();
builder.Services.AddScoped<IMemoryGraphService, MemoryGraphService>();
builder.Services.AddScoped<ISettingsDashboardService, SettingsDashboardService>();
builder.Services.AddScoped<IMessageProcessor, AgentMessageProcessor>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.MapOpenApi();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapDashboardEndpoints();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;

        return;
    }

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Run();
