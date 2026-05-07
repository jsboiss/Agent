using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Notifications;
using Agent.Providers;
using Agent.Resources;
using Agent.Settings;
using Agent.Tokens;
using Agent.Tools;
using Agent.Workspaces;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agent.Messages;

public sealed class AgentMessageProcessor(
    IAgentProviderSelector providerSelector,
    IConversationResolver conversationResolver,
    IConversationRepository conversationRepository,
    IAgentResourceLoader resourceLoader,
    IConversationPromptQueue promptQueue,
    IAgentSettingsResolver settingsResolver,
    IWebHostEnvironment environment,
    IAgentToolExecutor toolExecutor,
    IMemoryScout memoryScout,
    IMemoryExtractor memoryExtractor,
    IMemoryCandidateReviewer memoryCandidateReviewer,
    IMemoryStore memoryStore,
    IAgentEventSink eventSink,
    IAgentWorkspaceStore workspaceStore,
    IAgentRunStore runStore,
    IConversationMirrorStore mirrorStore,
    IAgentMessageRouter messageRouter,
    IAgentTokenTracker tokenTracker,
    IAgentNotifier notifier) : IMessageProcessor
{
    private static int MaxToolIterations => 3;

    private sealed class EventPublishCursor
    {
        public int PublishedCount { get; set; }
    }

    public async Task<MessageResult> Process(MessageRequest request, CancellationToken cancellationToken)
    {
        var resolution = await conversationResolver.Resolve(
            new ConversationResolveRequest(request.Channel, request.ConversationId),
            cancellationToken);
        var conversation = resolution.Conversation;
        var rootPath = WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath);
        var workspaceResolution = await workspaceStore.GetOrCreateActive(rootPath, cancellationToken);
        var workspace = workspaceResolution.Workspace;
        Directory.CreateDirectory(workspace.RootPath);
        workspace = await ClearStaleActiveRun(workspace, cancellationToken);
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                conversation,
                request.Channel,
                workspace.RootPath,
                request.Overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            cancellationToken);
        var route = await messageRouter.Resolve(
            workspace,
            request.Channel,
            request.UserMessage,
            cancellationToken);
        var trustedChannel = IsTrustedChannel(request);
        var notificationTarget = GetNotificationTarget(request);
        var started = promptQueue.TryStart(conversation.Id);
        var queueKind = promptQueue.Classify(request.UserMessage, !started);
        List<AgentEvent> events =
        [
            GetEvent(
                AgentEventKind.MessageReceived,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["channel"] = request.Channel,
                    ["conversationCreated"] = resolution.Created.ToString(),
                    ["conversationKind"] = conversation.Kind.ToString(),
                    ["workspaceId"] = workspace.Id,
                    ["workspaceCreated"] = workspaceResolution.Created.ToString(),
                    ["routeKind"] = route.RouteKind.ToString(),
                    ["remoteExecutionAllowed"] = workspace.RemoteExecutionAllowed.ToString(),
                    ["trustedChannel"] = trustedChannel.ToString(),
                    ["queueKind"] = queueKind.ToString(),
                    ["queueBehavior"] = settings.Get("queue.behavior") ?? string.Empty,
                    ["queued"] = (!started).ToString(),
                    ["receivedAt"] = request.ReceivedAt.ToString("O"),
                    ["message"] = request.UserMessage
                })
        ];
        var eventCursor = new EventPublishCursor();
        await PublishPending(events, eventCursor, cancellationToken);

        var userEntry = await conversationRepository.AddEntry(
            conversation.Id,
            ConversationEntryRole.User,
            request.Channel,
            request.UserMessage,
            null,
            cancellationToken);

        events.Add(GetEvent(
            AgentEventKind.MessagePersisted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["role"] = ConversationEntryRole.User.ToString(),
                ["channel"] = request.Channel,
                ["ConversationEntryId"] = userEntry.Id,
                ["queueKind"] = queueKind.ToString(),
                ["message"] = request.UserMessage
            }));
        await PublishPending(events, eventCursor, cancellationToken);

        if (!trustedChannel)
        {
            return await Refuse(
                conversation.Id,
                request.Channel,
                userEntry.Id,
                "This remote sender is not trusted, so the message was rejected before reaching Codex.",
                events,
                eventCursor,
                cancellationToken,
                queueKind);
        }

        var explicitMemoryResult = await TryHandleExplicitMemoryRequest(
            conversation.Id,
            request.Channel,
            userEntry,
            settings,
            events,
            eventCursor,
            cancellationToken);

        if (explicitMemoryResult is not null)
        {
            promptQueue.Complete(conversation.Id);
            return explicitMemoryResult;
        }

        if (route.RouteKind != AgentRouteKind.Chat && !route.AllowsMutation)
        {
            return await Refuse(
                conversation.Id,
                request.Channel,
                userEntry.Id,
                "Remote execution is disabled for the active workspace. Set RemoteExecutionAllowed = true for this workspace before sending code-changing or command-running requests from this channel.",
                events,
                eventCursor,
                cancellationToken,
                queueKind);
        }

        if (!started)
        {
            var queuedMessage = promptQueue.Enqueue(
                conversation.Id,
                userEntry.Id,
                queueKind,
                request.Channel,
                request.UserMessage);

            events.Add(GetEvent(
                AgentEventKind.MessagePersisted,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["queuedMessageId"] = queuedMessage.Id,
                    ["queueKind"] = queuedMessage.Kind.ToString(),
                    ["ConversationEntryId"] = userEntry.Id,
                    ["message"] = "Message queued while conversation is active."
                }));
            await PublishPending(events, eventCursor, cancellationToken);

            return await Complete(
                conversation.Id,
                string.Empty,
                events,
                eventCursor,
                cancellationToken,
                queueKind,
                true);
        }

        AgentRun? activeRun = null;

        if (route.RouteKind == AgentRouteKind.Work)
        {
            activeRun = await runStore.Create(
                workspace.Id,
                request.UserMessage,
                AgentRunKind.Work,
                request.Channel,
                null,
                workspace.WorkThreadId,
                cancellationToken);
            workspace = await workspaceStore.SetActiveRun(workspace.Id, activeRun.Id, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(route.RunId))
        {
            activeRun = await runStore.Get(route.RunId, cancellationToken);
        }

        var providerType = GetProviderType(settings);
        var provider = providerSelector.Get(providerType);
        var resources = await resourceLoader.Load(
            new AgentResourceLoadRequest(conversation, request.Channel, providerType, settings, workspace.RootPath),
            cancellationToken);
        var memoryScoutResult = await PrefetchMemory(
            conversation.Id,
            request,
            userEntry.Id,
            settings,
            events,
            eventCursor,
            cancellationToken);

        var recentMirrors = string.IsNullOrWhiteSpace(route.CodexThreadId)
            ? []
            : await mirrorStore.ListRecent(
                workspace.Id,
                route.CodexThreadId,
                8,
                cancellationToken);
        var providerRequest = new AgentProviderRequest(
            providerType,
            conversation.Id,
            request.UserMessage,
            resources,
            memoryScoutResult.CompactContext,
            memoryScoutResult.Memories,
            resources.Workspace.AvailableTools,
            [],
            [],
            workspace.RootPath,
            route.CodexThreadId,
            route.RouteKind,
            activeRun?.Id,
            settings.Get("codex.sandbox") ?? "danger-full-access",
            settings.Get("codex.approvalPolicy") ?? "never",
            GetMirrorContext(recentMirrors),
            resources.ChannelInstructions,
            route.AllowsMutation);

        try
        {
            if (activeRun is not null)
            {
                await runStore.Update(
                    activeRun.Id,
                    AgentRunStatus.Running,
                    route.CodexThreadId,
                    null,
                    null,
                    cancellationToken);
            }

            var providerResult = await RunProviderToolLoop(
                provider,
                providerRequest,
                request.Channel,
                userEntry.Id,
                events,
                eventCursor,
                settings,
                notificationTarget,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(providerResult.AssistantMessage))
            {
                var assistantEntry = await conversationRepository.AddEntry(
                    conversation.Id,
                    ConversationEntryRole.Assistant,
                    request.Channel,
                    providerResult.AssistantMessage,
                    userEntry.Id,
                    cancellationToken);

                events.Add(GetEvent(
                    AgentEventKind.MessagePersisted,
                    conversation.Id,
                    new Dictionary<string, string>
                    {
                        ["role"] = ConversationEntryRole.Assistant.ToString(),
                        ["channel"] = request.Channel,
                        ["ConversationEntryId"] = assistantEntry.Id,
                        ["ParentEntryId"] = userEntry.Id,
                        ["message"] = providerResult.AssistantMessage
                    }));
                await PublishPending(events, eventCursor, cancellationToken);

                await PersistCodexState(
                    workspace,
                    activeRun,
                    route,
                    providerResult,
                    request.Channel,
                    request.UserMessage,
                    providerResult.AssistantMessage,
                    cancellationToken);

                await ExtractMemories(
                    conversation.Id,
                    userEntry,
                    assistantEntry,
                    memoryScoutResult.Memories,
                    settings,
                    events,
                    eventCursor,
                    cancellationToken);
            }

            return await Complete(
                conversation.Id,
                providerResult.AssistantMessage,
                events,
                eventCursor,
                cancellationToken,
                queueKind);
        }
        finally
        {
            if (activeRun is not null)
            {
                await workspaceStore.SetActiveRun(workspace.Id, null, cancellationToken);
            }

            promptQueue.Complete(conversation.Id);
        }
    }

    private async Task PersistCodexState(
        AgentWorkspace workspace,
        AgentRun? run,
        AgentRouteResolution route,
        AgentProviderResult providerResult,
        string channel,
        string userMessage,
        string assistantMessage,
        CancellationToken cancellationToken)
    {
        var threadId = providerResult.CodexThreadId
            ?? providerResult.UsageMetadata.GetValueOrDefault("codexThreadId")
            ?? route.CodexThreadId;

        if (string.IsNullOrWhiteSpace(threadId))
        {
            if (run is not null)
            {
                await runStore.Update(
                    run.Id,
                    AgentRunStatus.Failed,
                    null,
                    null,
                    providerResult.Error ?? "Codex did not return a thread id.",
                    cancellationToken);
            }

            return;
        }

        if (route.RouteKind is AgentRouteKind.Chat or AgentRouteKind.Work)
        {
            await workspaceStore.SetThreadId(
                workspace.Id,
                route.RouteKind,
                threadId,
                cancellationToken);
        }

        if (run is not null)
        {
            await runStore.Update(
                run.Id,
                string.IsNullOrWhiteSpace(providerResult.Error) ? AgentRunStatus.Completed : AgentRunStatus.Failed,
                threadId,
                assistantMessage,
                providerResult.Error,
                cancellationToken);
        }

        await mirrorStore.Add(
            workspace.Id,
            run?.Id,
            threadId,
            channel,
            ConversationEntryRole.User,
            userMessage,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(assistantMessage))
        {
            await mirrorStore.Add(
                workspace.Id,
                run?.Id,
                threadId,
                channel,
                ConversationEntryRole.Assistant,
                assistantMessage,
                cancellationToken);
        }
    }

    private async Task<AgentWorkspace> ClearStaleActiveRun(
        AgentWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspace.ActiveRunId))
        {
            return workspace;
        }

        var activeRun = await runStore.Get(workspace.ActiveRunId, cancellationToken);

        if (activeRun is null)
        {
            return await workspaceStore.SetActiveRun(workspace.Id, null, cancellationToken);
        }

        if (activeRun.Status is not (AgentRunStatus.Created or AgentRunStatus.Running))
        {
            return await workspaceStore.SetActiveRun(workspace.Id, null, cancellationToken);
        }

        if (activeRun.StartedAt > DateTimeOffset.UtcNow.AddMinutes(-2))
        {
            return workspace;
        }

        await runStore.Update(
            activeRun.Id,
            AgentRunStatus.Failed,
            activeRun.CodexThreadId,
            activeRun.FinalResponse,
            "Run was marked stale after 2 minutes without completion.",
            cancellationToken);

        return await workspaceStore.SetActiveRun(workspace.Id, null, cancellationToken);
    }

    private async Task<MessageResult> Complete(
        string conversationId,
        string assistantMessage,
        IReadOnlyList<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken,
        QueuedMessageKind queueKind,
        bool queued = false)
    {
        await PublishPending(events, eventCursor, cancellationToken);

        return new MessageResult(
            conversationId,
            assistantMessage,
            events,
            queueKind,
            queued);
    }

    private async Task<MessageResult> Refuse(
        string conversationId,
        string channel,
        string userEntryId,
        string refusal,
        List<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken,
        QueuedMessageKind queueKind)
    {
        var assistantEntry = await conversationRepository.AddEntry(
            conversationId,
            ConversationEntryRole.Assistant,
            channel,
            refusal,
            userEntryId,
            cancellationToken);

        events.Add(GetEvent(
            AgentEventKind.ProviderError,
            conversationId,
            new Dictionary<string, string>
            {
                ["channel"] = channel,
                ["ConversationEntryId"] = assistantEntry.Id,
                ["ParentEntryId"] = userEntryId,
                ["error"] = refusal
            }));

        return await Complete(
            conversationId,
            refusal,
            events,
            eventCursor,
            cancellationToken,
            queueKind);
    }

    private async Task PublishPending(
        IReadOnlyList<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken)
    {
        while (eventCursor.PublishedCount < events.Count)
        {
            await eventSink.Publish(events[eventCursor.PublishedCount], cancellationToken);
            eventCursor.PublishedCount++;
        }
    }

    private async Task<AgentProviderResult> RunProviderToolLoop(
        IAgentProviderClient provider,
        AgentProviderRequest initialRequest,
        string channel,
        string userEntryId,
        List<AgentEvent> events,
        EventPublishCursor eventCursor,
        AgentSettings settings,
        string? notificationTarget,
        CancellationToken cancellationToken)
    {
        var providerRequest = initialRequest;
        AgentProviderResult? providerResult = null;
        List<AgentProviderToolCall> priorToolCalls = [];
        List<AgentProviderToolResult> providerToolResults = [];
        HashSet<string> executedToolKeys = new(StringComparer.OrdinalIgnoreCase);

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            events.Add(GetProviderRequestStartedEvent(providerRequest, channel, userEntryId, iteration));
            await PublishPending(events, eventCursor, cancellationToken);
            providerResult = await provider.Send(providerRequest, cancellationToken);
            var mainContext = await conversationRepository.ListEntries(providerRequest.ConversationId, cancellationToken);
            var tokenUsage = tokenTracker.Measure(providerRequest, providerResult, settings, mainContext);
            AddProviderEvents(providerResult, providerRequest, userEntryId, iteration, tokenUsage, events);
            await PublishPending(events, eventCursor, cancellationToken);

            var delegationToolCall = GetDelegationToolCall(providerResult.AssistantMessage, userEntryId);

            if (providerResult.ToolCalls.Count == 0 && delegationToolCall is not null)
            {
                await SendDelegationAck(channel, notificationTarget, cancellationToken);
                var delegationResults = await ExecuteToolCalls(
                    [delegationToolCall],
                    providerRequest.ConversationId,
                    channel,
                    userEntryId,
                    notificationTarget,
                    executedToolKeys,
                    events,
                    eventCursor,
                    cancellationToken);
                var assistantMessage = StripDelegationDirective(providerResult.AssistantMessage);

                return providerResult with
                {
                    AssistantMessage = string.IsNullOrWhiteSpace(assistantMessage)
                        ? "Background sub-agent queued."
                        : assistantMessage
                };
            }

            if (providerResult.ToolCalls.Count == 0)
            {
                return string.IsNullOrWhiteSpace(providerResult.AssistantMessage)
                    ? providerResult with { AssistantMessage = GetToolLoopFallback(providerResult, providerToolResults) }
                    : providerResult;
            }

            var toolResults = await ExecuteToolCalls(
                providerResult.ToolCalls,
                providerRequest.ConversationId,
                channel,
                userEntryId,
                notificationTarget,
                executedToolKeys,
                events,
                eventCursor,
                cancellationToken);

            priorToolCalls.AddRange(providerResult.ToolCalls);
            providerToolResults.AddRange(toolResults);

            providerRequest = providerRequest with
            {
                PriorToolCalls = priorToolCalls.ToArray(),
                ToolResults = providerToolResults.ToArray()
            };
        }

        return providerResult is null
            ? new AgentProviderResult(string.Empty, [], new Dictionary<string, string>(), "Provider loop did not run.")
            : providerResult with
            {
                AssistantMessage = string.IsNullOrWhiteSpace(providerResult.AssistantMessage)
                    ? GetToolLoopFallback(providerResult, providerToolResults)
                    : providerResult.AssistantMessage
            };
    }

    private async Task ExtractMemories(
        string conversationId,
        ConversationEntry userEntry,
        ConversationEntry assistantEntry,
        IReadOnlyList<MemoryRecord> injectedMemories,
        AgentSettings settings,
        List<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken)
    {
        if (string.Equals(settings.Get("memory.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(settings.Get("memory.extraction.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        events.Add(GetEvent(
            AgentEventKind.MemoryExtractionStarted,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntry.Id,
                ["mode"] = settings.Get("memory.extraction.mode") ?? string.Empty,
                ["provider"] = settings.Get("memory.extraction.provider") ?? string.Empty
            }));
        await PublishPending(events, eventCursor, cancellationToken);
        var extraction = await memoryExtractor.Extract(
            new MemoryExtractionRequest(
                conversationId,
                userEntry,
                assistantEntry,
                [],
                injectedMemories,
                settings.Values),
            cancellationToken);

        var reviewResult = await memoryCandidateReviewer.Review(
            new MemoryCandidateReviewRequest(conversationId, extraction.Memories),
            cancellationToken);
        var written = 0;
        var skipped = 0;

        foreach (var review in reviewResult.Reviews)
        {
            if (!review.Accepted)
            {
                skipped++;
                continue;
            }

            var extractedMemory = review.Candidate;
            var memory = await memoryStore.Write(
                new MemoryWriteRequest(
                    extractedMemory.Text,
                    extractedMemory.Tier,
                    extractedMemory.Segment,
                    extractedMemory.Importance,
                    extractedMemory.Confidence,
                    extractedMemory.SourceMessageId),
                cancellationToken);
            written++;

            events.Add(GetEvent(
                AgentEventKind.MemoryWrite,
                conversationId,
                new Dictionary<string, string>
                {
                    ["ConversationEntryId"] = userEntry.Id,
                    ["memoryId"] = memory.Id,
                    ["tier"] = memory.Tier.ToString(),
                    ["segment"] = memory.Segment.ToString(),
                    ["reviewScore"] = review.Score.ToString("0.###"),
                    ["text"] = memory.Text
                }));
            await PublishPending(events, eventCursor, cancellationToken);
        }

        events.Add(GetEvent(
            AgentEventKind.MemoryExtractionCompleted,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntry.Id,
                ["proposedCount"] = extraction.Memories.Count.ToString(),
                ["writtenCount"] = written.ToString(),
                ["skippedCount"] = skipped.ToString(),
                ["reviewedCount"] = reviewResult.Reviews.Count.ToString(),
                ["reviewSummary"] = string.Join("; ", reviewResult.Reviews.Select(x => $"{x.Score:0.##}:{x.Reason}"))
            }));
        await PublishPending(events, eventCursor, cancellationToken);
    }

    private async Task<MemoryScoutResult> PrefetchMemory(
        string conversationId,
        MessageRequest request,
        string userEntryId,
        AgentSettings settings,
        List<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken)
    {
        if (string.Equals(settings.Get("memory.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return new MemoryScoutResult(false, [], string.Empty);
        }

        events.Add(GetEvent(
            AgentEventKind.MemoryScoutStarted,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntryId,
                ["channel"] = request.Channel
            }));
        await PublishPending(events, eventCursor, cancellationToken);
        var result = await memoryScout.Prefetch(
            new MemoryScoutRequest(
                conversationId,
                request.UserMessage,
                new Dictionary<string, string>
                {
                    ["channel"] = request.Channel,
                    ["ConversationEntryId"] = userEntryId,
                    ["limit"] = settings.Get("memory.scoutLimit") ?? "5"
                }),
            cancellationToken);
        events.Add(GetEvent(
            AgentEventKind.MemoryScoutCompleted,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntryId,
                ["memoryCount"] = result.Memories.Count.ToString(),
                ["isMemoryRelevant"] = result.IsMemoryRelevant.ToString()
            }));
        await PublishPending(events, eventCursor, cancellationToken);

        if (result.IsMemoryRelevant)
        {
            events.Add(GetEvent(
                AgentEventKind.MemoryInjected,
                conversationId,
                new Dictionary<string, string>
                {
                    ["ConversationEntryId"] = userEntryId,
                    ["memoryIds"] = string.Join(",", result.Memories.Select(x => x.Id))
                }));
            await PublishPending(events, eventCursor, cancellationToken);
        }

        return result;
    }

    private async Task<MessageResult?> TryHandleExplicitMemoryRequest(
        string conversationId,
        string channel,
        ConversationEntry userEntry,
        AgentSettings settings,
        List<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken)
    {
        if (!IsExplicitRememberThat(userEntry.Content)
            || string.Equals(settings.Get("memory.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var entries = await conversationRepository.ListEntries(conversationId, cancellationToken);
        var sourceEntry = entries
            .Where(x => !string.Equals(x.Id, userEntry.Id, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Role is ConversationEntryRole.User or ConversationEntryRole.Assistant)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (sourceEntry is null)
        {
            return null;
        }

        var memoryText = GetMemoryTextFromPriorEntry(sourceEntry.Content);

        if (string.IsNullOrWhiteSpace(memoryText))
        {
            return null;
        }

        var memory = await memoryStore.Write(
            new MemoryWriteRequest(
                memoryText,
                MemoryTier.Long,
                GetSegmentForExplicitMemory(memoryText),
                0.8,
                0.9,
                sourceEntry.Id),
            cancellationToken);
        events.Add(GetEvent(
            AgentEventKind.MemoryWrite,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntry.Id,
                ["sourceEntryId"] = sourceEntry.Id,
                ["memoryId"] = memory.Id,
                ["tier"] = memory.Tier.ToString(),
                ["segment"] = memory.Segment.ToString(),
                ["text"] = memory.Text
            }));

        var assistantMessage = $"Saved that to memory: {memory.Text}";
        var assistantEntry = await conversationRepository.AddEntry(
            conversationId,
            ConversationEntryRole.Assistant,
            channel,
            assistantMessage,
            userEntry.Id,
            cancellationToken);
        events.Add(GetEvent(
            AgentEventKind.MessagePersisted,
            conversationId,
            new Dictionary<string, string>
            {
                ["role"] = ConversationEntryRole.Assistant.ToString(),
                ["channel"] = channel,
                ["ConversationEntryId"] = assistantEntry.Id,
                ["ParentEntryId"] = userEntry.Id,
                ["message"] = assistantMessage
            }));

        return await Complete(
            conversationId,
            assistantMessage,
            events,
            eventCursor,
            cancellationToken,
            QueuedMessageKind.Prompt);
    }

    private static bool IsExplicitRememberThat(string value)
    {
        var normalized = value.Trim().TrimEnd('.', '!', '?');

        return string.Equals(normalized, "remember that", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "remember this", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "save that", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "save this", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMemoryTextFromPriorEntry(string content)
    {
        var value = content.Trim();
        var prefixIndex = value.IndexOf("your ", StringComparison.OrdinalIgnoreCase);

        if (prefixIndex >= 0)
        {
            value = "User's " + value[(prefixIndex + "your ".Length)..];
        }

        return value
            .Trim()
            .Trim('"')
            .TrimEnd('.');
    }

    private static MemorySegment GetSegmentForExplicitMemory(string text)
    {
        return text.Contains(" name is ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("girlfriend", StringComparison.OrdinalIgnoreCase)
            || text.Contains("boyfriend", StringComparison.OrdinalIgnoreCase)
            || text.Contains("partner", StringComparison.OrdinalIgnoreCase)
            ? MemorySegment.Relationship
            : MemorySegment.Context;
    }

    private async Task<IReadOnlyList<AgentProviderToolResult>> ExecuteToolCalls(
        IReadOnlyList<AgentProviderToolCall> toolCalls,
        string conversationId,
        string channel,
        string userEntryId,
        string? notificationTarget,
        ISet<string> executedToolKeys,
        List<AgentEvent> events,
        EventPublishCursor eventCursor,
        CancellationToken cancellationToken)
    {
        List<AgentProviderToolResult> results = [];

        foreach (var toolCall in toolCalls)
        {
            var toolKey = GetToolKey(toolCall);

            events.Add(GetEvent(
                AgentEventKind.ToolCallStarted,
                conversationId,
                new Dictionary<string, string>
                {
                    ["toolCallId"] = toolCall.Id,
                    ["toolName"] = toolCall.Name,
                    ["ConversationEntryId"] = userEntryId
                }));
            await PublishPending(events, eventCursor, cancellationToken);

            AgentToolResult toolResult;

            if (!executedToolKeys.Add(toolKey))
            {
                toolResult = new AgentToolResult(
                    toolCall.Name,
                    false,
                    $"Skipped duplicate tool call '{toolCall.Name}' in the same turn.",
                    new Dictionary<string, string>
                    {
                        ["duplicate"] = "true"
                    });
            }
            else
            {
                toolResult = await toolExecutor.Execute(
                    new AgentToolRequest(
                toolCall.Name,
                AddNotificationTarget(toolCall.Arguments, notificationTarget),
                conversationId,
                channel,
                userEntryId),
                    cancellationToken);
            }

            var toolEntry = await conversationRepository.AddEntry(
                conversationId,
                ConversationEntryRole.Tool,
                channel,
                toolResult.Content,
                userEntryId,
                cancellationToken);

            events.Add(GetEvent(
                AgentEventKind.ToolCallOutput,
                conversationId,
                new Dictionary<string, string>
                {
                    ["toolCallId"] = toolCall.Id,
                    ["toolName"] = toolCall.Name,
                    ["ConversationEntryId"] = toolEntry.Id,
                    ["ParentEntryId"] = userEntryId,
                    ["output"] = toolResult.Content
                }));
            events.Add(GetEvent(
                AgentEventKind.ToolCallCompleted,
                conversationId,
                new Dictionary<string, string>
                {
                    ["toolCallId"] = toolCall.Id,
                    ["toolName"] = toolCall.Name,
                    ["ConversationEntryId"] = toolEntry.Id,
                    ["succeeded"] = toolResult.Succeeded.ToString()
                }));
            await PublishPending(events, eventCursor, cancellationToken);

            results.Add(new AgentProviderToolResult(
                toolCall.Id,
                toolCall.Name,
                toolResult.Content));
        }

        return results;
    }

    private static AgentProviderToolCall? GetDelegationToolCall(string assistantMessage, string userEntryId)
    {
        var json = GetDelegationJson(assistantMessage);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            var task = node?["task"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(task))
            {
                return null;
            }

            var capabilities = node?["capabilities"]?.GetValue<string>();
            var requiresConfirmation = node?["requiresConfirmation"]?.GetValue<bool?>();

            return new AgentProviderToolCall(
                $"delegate-{Guid.NewGuid():N}",
                "spawn_agent",
                new Dictionary<string, string>
                {
                    ["task"] = task,
                    ["parentEntryId"] = userEntryId,
                    ["capabilities"] = string.IsNullOrWhiteSpace(capabilities) ? "ReadOnly,Code" : capabilities,
                    ["requiresConfirmation"] = (requiresConfirmation ?? true).ToString()
                });
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string StripDelegationDirective(string assistantMessage)
    {
        var start = assistantMessage.IndexOf(
            "<delegate_to_sub_agent>",
            StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return assistantMessage;
        }

        var end = assistantMessage.IndexOf(
            "</delegate_to_sub_agent>",
            start,
            StringComparison.OrdinalIgnoreCase);

        if (end < 0)
        {
            return assistantMessage[..start].Trim();
        }

        var afterEnd = end + "</delegate_to_sub_agent>".Length;
        return (assistantMessage[..start] + assistantMessage[afterEnd..]).Trim();
    }

    private async Task SendDelegationAck(
        string channel,
        string? notificationTarget,
        CancellationToken cancellationToken)
    {
        if (!IsMobileChannel(channel))
        {
            return;
        }

        await notifier.Send(
            channel,
            notificationTarget,
            "Queued that for a background Codex run.",
            cancellationToken);
    }

    private static bool IsMobileChannel(string channel)
    {
        return string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "imessage", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetDelegationJson(string assistantMessage)
    {
        var startTag = "<delegate_to_sub_agent>";
        var endTag = "</delegate_to_sub_agent>";
        var start = assistantMessage.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return null;
        }

        var contentStart = start + startTag.Length;
        var end = assistantMessage.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);

        return end < 0
            ? null
            : assistantMessage[contentStart..end].Trim();
    }

    private static string? GetNotificationTarget(MessageRequest request)
    {
        if (request.Overrides?.TryGetValue("telegramChatId", out var telegramChatId) == true)
        {
            return telegramChatId;
        }

        if (request.Overrides?.TryGetValue("notificationTarget", out var notificationTarget) == true)
        {
            return notificationTarget;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> AddNotificationTarget(
        IReadOnlyDictionary<string, string> arguments,
        string? notificationTarget)
    {
        if (string.IsNullOrWhiteSpace(notificationTarget)
            || arguments.ContainsKey("notificationTarget")
            || arguments.ContainsKey("target"))
        {
            return arguments;
        }

        var values = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase)
        {
            ["notificationTarget"] = notificationTarget,
            ["target"] = notificationTarget
        };

        return values;
    }

    private static string GetToolLoopFallback(
        AgentProviderResult providerResult,
        IReadOnlyList<AgentProviderToolResult> toolResults)
    {
        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            return providerResult.Error;
        }

        if (toolResults.Count == 0)
        {
            return "The provider returned no assistant message.";
        }

        if (toolResults.All(x => string.Equals(x.Name, "write_memory", StringComparison.OrdinalIgnoreCase)))
        {
            return toolResults.Any(x => x.Content.StartsWith("Memory already exists", StringComparison.OrdinalIgnoreCase))
                ? "That memory is already saved."
                : "I've saved that to memory.";
        }

        return string.Join(Environment.NewLine, toolResults.Select(x => x.Content));
    }

    private static bool IsTrustedChannel(MessageRequest request)
    {
        if (string.Equals(request.Channel, "local-web", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.Overrides?.TryGetValue("trustedSender", out var trustedSender) == true
            && string.Equals(trustedSender, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMirrorContext(IReadOnlyList<ConversationMirrorEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            entries.Select(x => $"- {x.Role}: {x.Content}"));
    }

    private static string GetToolKey(AgentProviderToolCall toolCall)
    {
        return toolCall.Name + ":"
            + string.Join(
                "|",
                toolCall.Arguments
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Key}={x.Value}"));
    }

    private static AgentEvent GetProviderRequestStartedEvent(
        AgentProviderRequest request,
        string channel,
        string userEntryId,
        int iteration)
    {
        return GetEvent(
            AgentEventKind.ProviderRequestStarted,
            request.ConversationId,
            new Dictionary<string, string>
            {
                ["provider"] = request.Kind.ToString(),
                ["channel"] = channel,
                ["ConversationEntryId"] = userEntryId,
                ["iteration"] = iteration.ToString()
            });
    }

    private void AddProviderEvents(
        AgentProviderResult providerResult,
        AgentProviderRequest request,
        string userEntryId,
        int iteration,
        AgentTokenUsage tokenUsage,
        List<AgentEvent> events)
    {
        if (!string.IsNullOrWhiteSpace(providerResult.AssistantMessage))
        {
            events.Add(GetEvent(
                AgentEventKind.ProviderTextDelta,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["provider"] = request.Kind.ToString(),
                    ["ConversationEntryId"] = userEntryId,
                    ["iteration"] = iteration.ToString(),
                    ["text"] = providerResult.AssistantMessage
                }));
        }

        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            events.Add(GetEvent(
                AgentEventKind.ProviderError,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["provider"] = request.Kind.ToString(),
                    ["ConversationEntryId"] = userEntryId,
                    ["iteration"] = iteration.ToString(),
                    ["error"] = providerResult.Error
                }));
        }

        var data = new Dictionary<string, string>
            {
                ["provider"] = request.Kind.ToString(),
                ["ConversationEntryId"] = userEntryId,
                ["iteration"] = iteration.ToString(),
                ["toolCallCount"] = providerResult.ToolCalls.Count.ToString(),
                ["hasError"] = (!string.IsNullOrWhiteSpace(providerResult.Error)).ToString()
            };

        foreach (var x in tokenTracker.ToMetadata(tokenUsage))
        {
            data[x.Key] = x.Value;
        }

        events.Add(GetEvent(
            AgentEventKind.ProviderTurnCompleted,
            request.ConversationId,
            data));
    }

    private static AgentEvent GetEvent(
        AgentEventKind kind,
        string conversationId,
        IReadOnlyDictionary<string, string> data)
    {
        return new AgentEvent(
            Guid.NewGuid().ToString("N"),
            kind,
            conversationId,
            DateTimeOffset.UtcNow,
            data);
    }

    private static AgentProviderType GetProviderType(AgentSettings settings)
    {
        var provider = settings.Get("provider");

        if (Enum.TryParse<AgentProviderType>(provider, true, out var providerType))
        {
            return providerType;
        }

        return AgentProviderType.Ollama;
    }

}
