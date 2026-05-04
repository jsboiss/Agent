using Agent.Components;
using Agent.Endpoints;
using Agent.Providers;
using Agent.Providers.ClaudeCode;
using Agent.Providers.Codex;
using Agent.Providers.Ollama;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.Configure<OllamaProviderOptions>(
    builder.Configuration.GetSection(OllamaProviderOptions.SectionName));
builder.Services.AddHttpClient<OllamaProviderClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<OllamaProviderOptions>>().Value;
    OllamaProviderClient.ConfigureHttpClient(httpClient, options);
});
builder.Services.AddSingleton<IAgentProviderClient, ClaudeCodeProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient, CodexProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient>(x => x.GetRequiredService<OllamaProviderClient>());
builder.Services.AddSingleton<IAgentProviderSelector, AgentProviderSelector>();

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
