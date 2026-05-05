using Agent.Components;
using Agent.Compaction;
using Agent.Conversations;
using Agent.Endpoints;
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
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.Configure<OllamaProviderOptions>(
    builder.Configuration.GetSection(OllamaProviderOptions.SectionName));
builder.Services.Configure<SqliteMemoryOptions>(
    builder.Configuration.GetSection("Memory:Sqlite"));
builder.Services.AddHttpClient<OllamaProviderClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<OllamaProviderOptions>>().Value;
    OllamaProviderClient.ConfigureHttpClient(httpClient, options);
});
builder.Services.AddSingleton<IAgentProviderClient, ClaudeCodeProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient, CodexProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient>(x => x.GetRequiredService<OllamaProviderClient>());
builder.Services.AddSingleton<IAgentProviderSelector, AgentProviderSelector>();
builder.Services.AddSingleton<IConversationRepository, InMemoryConversationRepository>();
builder.Services.AddSingleton<IConversationResolver, ConversationResolver>();
builder.Services.AddSingleton<IConversationSummaryStore, InMemoryConversationSummaryStore>();
builder.Services.AddSingleton<IConversationCompactor, RollingConversationCompactor>();
builder.Services.AddSingleton<IAgentResourceLoader, AgentResourceLoader>();
builder.Services.AddSingleton<IConversationPromptQueue, InMemoryConversationPromptQueue>();
builder.Services.AddSingleton<IAgentSettingsResolver, ConfigurationAgentSettingsResolver>();
builder.Services.AddSingleton<ISubAgentCoordinator, SubAgentCoordinator>();
builder.Services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
builder.Services.AddSingleton<IMemoryScout, MemoryScout>();
builder.Services.AddSingleton<IAgentToolExecutor, AgentToolExecutor>();
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapDashboardEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
