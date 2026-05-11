using Agent.Authentication;
using Agent.Automations;
using Agent.Calendar;
using Agent.Capabilities;
using Agent.Channels.Telegram;
using Agent.Compaction;
using Agent.Context;
using Agent.Conversations;
using Agent.Dashboard;
using Agent.Drafts;
using Agent.Endpoints;
using Agent.Email;
using Agent.Events;
using Agent.Frontend;
using Agent.Integrations.Composio;
using Agent.Memory;
using Agent.Messages;
using Agent.Notifications;
using Agent.ProjectNotes;
using Agent.Providers;
using Agent.Providers.ClaudeCode;
using Agent.Providers.Codex;
using Agent.Providers.Gemini;
using Agent.Providers.Ollama;
using Agent.Resources;
using Agent.Settings;
using Agent.Skills;
using Agent.SubAgents;
using Agent.Tokens;
using Agent.Tools;
using Agent.Workspaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "MainAgent.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.Events.OnRedirectToLogin = context =>
        {
            if (IsApiRequest(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";

                return context.Response.WriteAsync("{\"error\":\"Authentication required.\"}");
            }

            context.Response.Redirect(context.RedirectUri);

            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (IsApiRequest(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";

                return context.Response.WriteAsync("{\"error\":\"Access denied.\"}");
            }

            context.Response.Redirect(context.RedirectUri);

            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.Configure<DashboardAuthOptions>(
    builder.Configuration.GetSection(DashboardAuthOptions.SectionName));
builder.Services.Configure<DevelopmentFrontendOptions>(
    builder.Configuration.GetSection(DevelopmentFrontendOptions.SectionName));
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<ViteDevelopmentFrontendService>();
}
builder.Services.Configure<OllamaProviderOptions>(
    builder.Configuration.GetSection(OllamaProviderOptions.SectionName));
builder.Services.Configure<GeminiProviderOptions>(
    builder.Configuration.GetSection(GeminiProviderOptions.SectionName));
builder.Services.Configure<ContextPlannerOptions>(
    builder.Configuration.GetSection(ContextPlannerOptions.SectionName));
builder.Services.Configure<CodexProviderOptions>(
    builder.Configuration.GetSection("Providers:Codex"));
builder.Services.Configure<SqliteMemoryOptions>(
    builder.Configuration.GetSection("Memory:Sqlite"));
builder.Services.Configure<SqliteAgentStateOptions>(
    builder.Configuration.GetSection("Agent:Sqlite"));
builder.Services.Configure<TelegramChannelOptions>(
    builder.Configuration.GetSection(TelegramChannelOptions.SectionName));
builder.Services.Configure<GoogleCalendarOptions>(
    builder.Configuration.GetSection(GoogleCalendarOptions.SectionName));
builder.Services.Configure<ComposioOptions>(
    builder.Configuration.GetSection(ComposioOptions.SectionName));
builder.Services.Configure<AgentHomeOptions>(
    builder.Configuration.GetSection(AgentHomeOptions.SectionName));
builder.Services.AddDataProtection();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddHttpClient<IComposioClient, ComposioClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<ComposioOptions>>().Value;
    ComposioClient.ConfigureHttpClient(httpClient, options);
});
builder.Services.AddSingleton<IGoogleCalendarClient, GoogleCalendarClient>();
builder.Services.AddHttpClient<OllamaProviderClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<OllamaProviderOptions>>().Value;
    OllamaProviderClient.ConfigureHttpClient(httpClient, options);
});
builder.Services.AddHttpClient<GeminiProviderClient>((services, httpClient) =>
{
    var options = services.GetRequiredService<IOptions<GeminiProviderOptions>>().Value;
    GeminiProviderClient.ConfigureHttpClient(httpClient, options);
});
builder.Services.AddSingleton<IAgentProviderClient, ClaudeCodeProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient, CodexProviderClient>();
builder.Services.AddSingleton<IAgentProviderClient>(x => x.GetRequiredService<OllamaProviderClient>());
builder.Services.AddSingleton<IAgentProviderClient>(x => x.GetRequiredService<GeminiProviderClient>());
builder.Services.AddSingleton<IAgentProviderSelector, AgentProviderSelector>();
builder.Services.AddSingleton<IAgentProviderToolLoop, AgentProviderToolLoop>();
builder.Services.AddSingleton<IConversationRepository, SqliteConversationRepository>();
builder.Services.AddSingleton<IConversationResolver, ConversationResolver>();
builder.Services.AddSingleton<IConversationSummaryStore, SqliteConversationSummaryStore>();
builder.Services.AddSingleton<IConversationCompactor, RollingConversationCompactor>();
builder.Services.AddSingleton<SqliteAgentStateStore>();
builder.Services.AddSingleton<IAgentWorkspaceStore>(x => x.GetRequiredService<SqliteAgentStateStore>());
builder.Services.AddSingleton<IAgentRunStore>(x => x.GetRequiredService<SqliteAgentStateStore>());
builder.Services.AddSingleton<IConversationMirrorStore>(x => x.GetRequiredService<SqliteAgentStateStore>());
builder.Services.AddSingleton<IAgentMessageRouter, AgentMessageRouter>();
builder.Services.AddSingleton<IAgentCapabilityRegistry, AgentCapabilityRegistry>();
builder.Services.AddSingleton<IAgentResourceLoader, AgentResourceLoader>();
builder.Services.AddSingleton<IProjectNoteStore, FileProjectNoteStore>();
builder.Services.AddSingleton<IProjectNoteDistiller, RuleBasedProjectNoteDistiller>();
builder.Services.AddSingleton<IAgentSkillStore, FileAgentSkillStore>();
builder.Services.AddSingleton<IConversationPromptQueue, InMemoryConversationPromptQueue>();
builder.Services.AddSingleton<IAgentSettingsResolver, ConfigurationAgentSettingsResolver>();
builder.Services.AddSingleton<ISubAgentWorkQueue, SubAgentWorkQueue>();
builder.Services.AddSingleton<ISubAgentCoordinator, SubAgentCoordinator>();
builder.Services.AddSingleton<IAgentTokenTracker, AgentTokenTracker>();
builder.Services.AddHostedService<InterruptedSubAgentRunCleanupService>();
builder.Services.AddHostedService<SubAgentRunWorker>();
builder.Services.AddSingleton<IAgentNotifier, CompositeAgentNotifier>();
builder.Services.AddSingleton<TelegramChatChannel>();
builder.Services.AddSingleton<IChannelNotifier>(x => x.GetRequiredService<TelegramChatChannel>());
builder.Services.AddHostedService(x => x.GetRequiredService<TelegramChatChannel>());
builder.Services.AddSingleton<IAgentEventStore, SqliteAgentEventStore>();
builder.Services.AddSingleton<IAgentEventSink>(x => x.GetRequiredService<IAgentEventStore>());
builder.Services.AddSingleton<IAgentDraftStore, SqliteAgentDraftStore>();
builder.Services.AddSingleton<IAutomationScheduler, SimpleAutomationScheduler>();
builder.Services.AddSingleton<IAutomationStore, SqliteAutomationStore>();
builder.Services.AddSingleton<IAutomationRunStore, SqliteAutomationRunStore>();
builder.Services.AddHostedService<AutomationWorker>();
builder.Services.AddSingleton<IGoogleCalendarAuthStore, SqliteGoogleCalendarAuthStore>();
builder.Services.AddSingleton<IComposioConnectionStore, SqliteComposioConnectionStore>();
builder.Services.AddSingleton<ICalendarProvider, GoogleCalendarProvider>();
builder.Services.AddSingleton<ICalendarScout, CalendarScout>();
builder.Services.AddSingleton<IEmailProvider, GmailProvider>();
builder.Services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
builder.Services.AddSingleton<IMemoryScout, MemoryScout>();
builder.Services.AddSingleton<IPromptMemorySnapshotBuilder, PromptMemorySnapshotBuilder>();
builder.Services.AddSingleton<IExternalMemoryProvider, NoOpExternalMemoryProvider>();
builder.Services.AddSingleton<RuleBasedContextPlanner>();
builder.Services.AddSingleton<IContextPlanner, GeminiContextPlanner>();
builder.Services.AddSingleton<IContextProvider, MemoryContextProvider>();
builder.Services.AddSingleton<IContextProvider, CalendarContextProvider>();
builder.Services.AddSingleton<IContextProvider, EmailContextProvider>();
builder.Services.AddSingleton<IContextOrchestrator, ContextOrchestrator>();
builder.Services.AddSingleton<RuleBasedMemoryExtractor>();
builder.Services.AddSingleton<LlmMemoryExtractor>();
builder.Services.AddSingleton<IMemoryExtractor, CompositeMemoryExtractor>();
builder.Services.AddSingleton<IMemoryCandidateReviewer, MemoryCandidateReviewer>();
builder.Services.AddSingleton<IMemoryMaintenanceService, MemoryMaintenanceService>();
builder.Services.AddHostedService<MemoryMaintenanceWorker>();
builder.Services.AddSingleton<ICompactionMemoryExtractor, CompactionMemoryExtractor>();
builder.Services.AddSingleton<IAgentToolExecutor, AgentToolExecutor>();
builder.Services.AddSingleton<IDashboardPasswordVerifier, ConfigurationDashboardPasswordVerifier>();
builder.Services.AddScoped<IChatDashboardService, ChatDashboardService>();
builder.Services.AddScoped<IMemoryDashboardService, MemoryDashboardService>();
builder.Services.AddScoped<IRunTimelineService, RunTimelineService>();
builder.Services.AddScoped<ITraceDashboardService, TraceDashboardService>();
builder.Services.AddScoped<ISubAgentDashboardService, SubAgentDashboardService>();
builder.Services.AddScoped<IMemoryGraphService, MemoryGraphService>();
builder.Services.AddScoped<ISettingsDashboardService, SettingsDashboardService>();
builder.Services.AddScoped<ICompactionDashboardService, CompactionDashboardService>();
builder.Services.AddScoped<IOperationsDashboardService, OperationsDashboardService>();
builder.Services.AddScoped<ICalendarDashboardService, CalendarDashboardService>();
builder.Services.AddScoped<IEmailDashboardService, EmailDashboardService>();
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
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapOpenApi();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (DashboardAuthEndpoints.AllowsAnonymousAccess(context.Request.Path)
        || context.User.Identity?.IsAuthenticated == true)
    {
        await next(context);

        return;
    }

    if (IsApiRequest(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync("{\"error\":\"Authentication required.\"}");

        return;
    }

    await context.ChallengeAsync();
});
app.UseAuthorization();
app.MapDashboardAuthEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev", (IOptions<DevelopmentFrontendOptions> options) =>
        Results.Redirect(options.Value.Url));
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSession();
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

static bool IsApiRequest(PathString path)
{
    return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);
}
