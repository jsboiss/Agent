namespace Agent.Resources;

public sealed record AgentResourceContext(
    WorkspaceContext Workspace,
    string GlobalInstructions,
    string WorkspaceInstructions,
    string ChannelInstructions,
    string ProviderConstraints,
    string PromptTemplate,
    string ToolContext,
    string CompactMemoryContext,
    string ConversationSummary)
{
    public string BuildSystemPrompt()
    {
        var sections = new List<string>
        {
            GlobalInstructions,
            WorkspaceInstructions,
            ChannelInstructions,
            ProviderConstraints,
            PromptTemplate,
            ToolContext,
            CompactMemoryContext,
            ConversationSummary
        };

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            sections.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
