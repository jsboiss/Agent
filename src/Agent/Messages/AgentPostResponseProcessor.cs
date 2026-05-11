using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.ProjectNotes;

namespace Agent.Messages;

public sealed class AgentPostResponseProcessor(
    IProjectNoteStore projectNoteStore,
    IProjectNoteDistiller projectNoteDistiller,
    IMemoryExtractor memoryExtractor,
    IMemoryCandidateReviewer memoryCandidateReviewer,
    IMemoryStore memoryStore,
    IAgentEventSink eventSink) : IAgentPostResponseProcessor
{
    public async Task Process(AgentPostResponseWorkItem item, CancellationToken cancellationToken)
    {
        await RecordProjectActivity(item, cancellationToken);
        await ExtractMemories(item, cancellationToken);
    }

    private async Task RecordProjectActivity(
        AgentPostResponseWorkItem item,
        CancellationToken cancellationToken)
    {
        var content = $"""
            User request:
            {Shorten(item.UserEntry.Content, 700)}

            Assistant result:
            {Shorten(item.AssistantEntry.Content, 1200)}
            """;

        await projectNoteStore.RecordActivity(
            item.Workspace,
            "main-thread",
            content,
            cancellationToken);

        await projectNoteDistiller.Distill(
            item.Workspace,
            new ProjectNoteDistillationRequest(
                "main-thread",
                item.UserEntry.Content,
                item.AssistantEntry.Content),
            cancellationToken);
    }

    private async Task ExtractMemories(
        AgentPostResponseWorkItem item,
        CancellationToken cancellationToken)
    {
        if (string.Equals(item.Settings.Get("memory.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(item.Settings.Get("memory.extraction.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await eventSink.Publish(GetEvent(
            AgentEventKind.MemoryExtractionStarted,
            item.ConversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = item.UserEntry.Id,
                ["mode"] = item.Settings.Get("memory.extraction.mode") ?? string.Empty,
                ["provider"] = item.Settings.Get("memory.extraction.provider") ?? string.Empty,
                ["model"] = item.Settings.Get("memory.extraction.model") ?? item.Settings.Get("model") ?? string.Empty,
                ["userMessageLength"] = item.UserEntry.Content.Length.ToString(),
                ["assistantMessageLength"] = item.AssistantEntry.Content.Length.ToString(),
                ["injectedMemoryCount"] = "0"
            }),
            cancellationToken);

        var extraction = await memoryExtractor.Extract(
            new MemoryExtractionRequest(
                item.ConversationId,
                item.UserEntry,
                item.AssistantEntry,
                [],
                [],
                item.Settings.Values),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(extraction.Error))
        {
            await eventSink.Publish(GetEvent(
                AgentEventKind.MemoryExtraction,
                item.ConversationId,
                new Dictionary<string, string>
                {
                    ["ConversationEntryId"] = item.UserEntry.Id,
                    ["status"] = "failed",
                    ["provider"] = extraction.Provider,
                    ["model"] = extraction.Model,
                    ["parseStatus"] = extraction.ParseStatus,
                    ["rawResponseLength"] = extraction.RawResponseLength.ToString(),
                    ["rawResponsePreview"] = extraction.RawResponsePreview,
                    ["error"] = extraction.Error
                }),
                cancellationToken);
        }

        var reviewResult = await memoryCandidateReviewer.Review(
            new MemoryCandidateReviewRequest(item.ConversationId, extraction.Memories),
            cancellationToken);
        var written = 0;
        var skipped = 0;
        var superseded = 0;

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
                    extractedMemory.SourceMessageId,
                    GetSupersedesValue(review)),
                cancellationToken);
            written++;

            foreach (var memoryId in review.SupersededMemoryIds)
            {
                await memoryStore.UpdateLifecycle(memoryId, MemoryLifecycle.Archived, cancellationToken);
                superseded++;
            }

            await eventSink.Publish(GetEvent(
                AgentEventKind.MemoryWrite,
                item.ConversationId,
                new Dictionary<string, string>
                {
                    ["ConversationEntryId"] = item.UserEntry.Id,
                    ["memoryId"] = memory.Id,
                    ["tier"] = memory.Tier.ToString(),
                    ["segment"] = memory.Segment.ToString(),
                    ["reviewScore"] = review.Score.ToString("0.###"),
                    ["supersededCount"] = review.SupersededMemoryIds.Count.ToString(),
                    ["supersedes"] = memory.Supersedes ?? string.Empty,
                    ["text"] = memory.Text
                }),
                cancellationToken);
        }

        await eventSink.Publish(GetEvent(
            AgentEventKind.MemoryExtractionCompleted,
            item.ConversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = item.UserEntry.Id,
                ["proposedCount"] = extraction.Memories.Count.ToString(),
                ["writtenCount"] = written.ToString(),
                ["skippedCount"] = skipped.ToString(),
                ["supersededCount"] = superseded.ToString(),
                ["reviewedCount"] = reviewResult.Reviews.Count.ToString(),
                ["provider"] = extraction.Provider,
                ["model"] = extraction.Model,
                ["parseStatus"] = extraction.ParseStatus,
                ["rawResponseLength"] = extraction.RawResponseLength.ToString(),
                ["rawResponsePreview"] = extraction.RawResponsePreview,
                ["error"] = extraction.Error ?? string.Empty,
                ["reviewSummary"] = string.Join("; ", reviewResult.Reviews.Select(x => $"{x.Score:0.##}:{x.Reason}"))
            }),
            cancellationToken);
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

    private static string? GetSupersedesValue(MemoryCandidateReview review)
    {
        return review.SupersededMemoryIds.Count == 0
            ? null
            : string.Join(",", review.SupersededMemoryIds);
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }
}

