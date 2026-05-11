import { useQueryClient } from "@tanstack/react-query";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from "react";
import { Gauge, SendHorizontal, Terminal } from "lucide-react";
import { getGetMainChatQueryKey, getMainChatResponse, sendMainChatMessage, useGetMainChat } from "../../api/generated";
import type { ChatDashboardMessage, TokenUsageBreakdown } from "../../api/generated";
import { EmptyState, ErrorState, formatLocalTime, IconButton, LoadingState, StatusChip, TopBar } from "../components";

const visibleMessageCount = 10;

export function ChatPage() {
  const queryClient = useQueryClient();
  const chatQuery = useGetMainChat({
    query: {
      refetchInterval: 2500
    }
  });
  const [prompt, setPrompt] = useState("");
  const [pendingPrompt, setPendingPrompt] = useState<string | null>(null);
  const [streamedText, setStreamedText] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const transcriptRef = useRef<HTMLDivElement | null>(null);
  const snapshot = chatQuery.data?.data;
  const messages = useMemo(() => {
    const loaded = snapshot?.messages ?? [];
    const optimistic = [...loaded];

    if (pendingPrompt && !hasLoadedPrompt(loaded, pendingPrompt)) {
      optimistic.push({
        id: `pending:user:${pendingPrompt}`,
        role: "You",
        content: pendingPrompt,
        htmlContent: pendingPrompt,
        createdAt: new Date().toISOString()
      });
    }

    if (streamedText) {
      optimistic.push({
        id: "pending:assistant",
        role: "Assistant",
        content: streamedText,
        htmlContent: streamedText,
        createdAt: new Date().toISOString()
      });
    }

    return optimistic;
  }, [pendingPrompt, snapshot?.messages, streamedText]);
  const visibleMessages = useMemo(() => messages.slice(-visibleMessageCount), [messages]);

  useEffect(() => {
    const transcript = transcriptRef.current;

    if (!transcript || visibleMessages.length === 0) {
      return;
    }

    requestAnimationFrame(() => {
      transcript.scrollTop = transcript.scrollHeight;
    });
  }, [visibleMessages, streamedText, isStreaming, errorMessage]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    const value = prompt.trim();

    if (!value || isStreaming) {
      return;
    }

    setPrompt("");
    setPendingPrompt(value);
    setStreamedText("");
    setIsStreaming(true);
    setErrorMessage(null);

    try {
      const response = await fetch("/api/dashboard/chat/main/stream", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ prompt: value })
      });

      if (!response.ok || !response.body) {
        const fallbackResponse = await sendMainChatMessage({ prompt: value });
        setErrorMessage(fallbackResponse.data.errorMessage);
        queryClient.setQueryData<getMainChatResponse>(getGetMainChatQueryKey(), {
          data: fallbackResponse.data.snapshot,
          status: fallbackResponse.status,
          headers: fallbackResponse.headers
        });

        return;
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let done = false;

      while (!done) {
        const result = await reader.read();
        done = result.done;

        if (result.value) {
          setStreamedText((x) => x + decoder.decode(result.value, { stream: !done }));
        }
      }

      const snapshotResponse = await fetch("/api/dashboard/chat/main");
      const snapshotData = await snapshotResponse.json();
      queryClient.setQueryData<getMainChatResponse>(getGetMainChatQueryKey(), {
        data: snapshotData,
        status: 200,
        headers: snapshotResponse.headers
      });
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Message send failed.");
    } finally {
      setPendingPrompt(null);
      setStreamedText("");
      setIsStreaming(false);
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      event.currentTarget.form?.requestSubmit();
    }
  }

  return (
    <section className="workspace chat-workspace">
      <div className="pane pane-chat chat-only">
        <TopBar
          eyebrow="Main conversation"
          title="Synthetic Intelligence Control"
          meta={snapshot && (
            <>
              <StatusChip label={snapshot.isRunning || isStreaming ? "processing" : "online"} tone={snapshot.isRunning || isStreaming ? "green" : "blue"} />
              <span>{snapshot.provider}</span>
              <strong>{snapshot.model}</strong>
              <TokenUsageHoverCard tokens={snapshot.tokens} tokenUsage={snapshot.tokenUsage} />
            </>
          )}
        />

        <div className="transcript" ref={transcriptRef}>
          {chatQuery.isLoading && <LoadingState />}
          {chatQuery.isError && <ErrorState error={chatQuery.error} />}
          {!chatQuery.isLoading && !chatQuery.isError && messages.length === 0 && (
            <EmptyState title="No messages yet" body="Start the main local-web conversation." />
          )}
          {messages.length > visibleMessages.length && (
            <div className="transcript-window-note">
              Showing latest {visibleMessages.length} of {messages.length} messages
            </div>
          )}
          {visibleMessages.map((message) => (
            <article className={`message ${getMessageClass(message.role)}`} key={message.id}>
              <header>
                <span className="avatar-square">{getAvatar(message.role)}</span>
                <strong>{getRoleLabel(message.role)}</strong>
                <time>{formatLocalTime(message.createdAt)}</time>
              </header>
              {isWorkMessage(message)
                ? <WorkMessage content={message.content} />
                : <MessageBody content={message.content} htmlContent={message.htmlContent} />}
            </article>
          ))}
          {isStreaming && !streamedText && (
            <article className="message from-agent typing-message" aria-live="polite">
              <header>
                <span className="avatar-square">
                  <Terminal size={13} />
                </span>
                <strong>Assistant</strong>
                <time></time>
              </header>
              <div className="work-state" aria-label="Waiting for assistant response">
                <span className="status-square active" />
                <span>provider stream initializing</span>
              </div>
            </article>
          )}
        </div>

        {errorMessage && <div className="callout error">{errorMessage}</div>}

        <form className="composer" onSubmit={submit}>
          <textarea
            disabled={isStreaming}
            onChange={(event) => setPrompt(event.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Message the agent"
            value={prompt}
          />
          <IconButton disabled={!prompt.trim() || isStreaming} title="Send message" type="submit">
            <SendHorizontal size={17} />
          </IconButton>
        </form>
      </div>
    </section>
  );
}

function getMessageClass(role: string) {
  const normalizedRole = normalizeRole(role);

  if (normalizedRole === "you") {
    return "from-user";
  }

  return isSubAgentRole(normalizedRole) ? "from-tool" : "from-agent";
}

function getAvatar(role: string) {
  const normalizedRole = normalizeRole(role);

  if (normalizedRole === "you") {
    return "Y";
  }

  return isSubAgentRole(normalizedRole) ? <Terminal size={13} /> : "AI";
}

function getRoleLabel(role: string) {
  const normalizedRole = normalizeRole(role);

  if (isSubAgentRole(normalizedRole)) {
    return "Sub-agent";
  }

  return normalizedRole === "assistant" ? "Main agent" : role;
}

function isSubAgentRole(role: string) {
  return role === "tool" || role === "sub-agent" || role === "subagent";
}

function isWorkMessage(message: ChatDashboardMessage) {
  return isSubAgentRole(normalizeRole(message.role))
    || message.content.startsWith("Sub-agent ")
    || message.content.startsWith("Sub-agent run ");
}

function normalizeRole(role: string) {
  return role.trim().toLowerCase().replaceAll("\u2011", "-").replaceAll("\u2013", "-").replaceAll("\u2014", "-");
}

function hasLoadedPrompt(messages: ChatDashboardMessage[], prompt: string) {
  return messages.some((x) => x.role === "You" && x.content.trim() === prompt);
}

function WorkMessage({ content }: { content: string }) {
  const queuedMatch = /^Sub-agent\s+(?<conversationId>[a-z0-9]+)\s+queued as background run\s+(?<runId>[a-z0-9]+):\s+(?<task>.*)$/s.exec(content);
  const completedMatch = /^Sub-agent run\s+(?<runId>[a-z0-9]+)\s+(?:completed|finished) with status\s+(?<status>\w+):\s+(?<result>.*)$/s.exec(content);

  if (queuedMatch?.groups) {
    return (
      <div className="work-message">
        <div className="work-message-meta">
          <span><span className="status-square active" />Queued</span>
          <code>{queuedMatch.groups.runId}</code>
        </div>
        <p>{shorten(queuedMatch.groups.task, 280)}</p>
      </div>
    );
  }

  if (completedMatch?.groups) {
    return (
      <div className="work-message">
        <div className="work-message-meta">
          <span><span className={`status-square ${completedMatch.groups.status === "Completed" ? "" : "error"}`} />{completedMatch.groups.status}</span>
          <code>{completedMatch.groups.runId}</code>
        </div>
        <p>{shorten(completedMatch.groups.result, 420)}</p>
      </div>
    );
  }

  return <p className="message-body">{content}</p>;
}

function MessageBody({ content, htmlContent }: { content: string; htmlContent: string }) {
  if (htmlContent && htmlContent !== content) {
    return <div className="message-body markdown-body" dangerouslySetInnerHTML={{ __html: htmlContent }} />;
  }

  return <p className="message-body">{content}</p>;
}

function TokenUsageHoverCard({
  tokens,
  tokenUsage
}: {
  tokens: {
    totalTokens: number | string;
    mainContextTokens: number | string;
    contextWindowTokens: number | string;
    remainingUntilCompactionTokens: number | string;
    source: string;
  };
  tokenUsage: TokenUsageBreakdown[];
}) {
  const codexUsage = getProviderUsage(tokenUsage, "Codex");
  const geminiUsage = getProviderUsage(tokenUsage, "Gemini");
  const contextWindowTokens = Number(tokens.contextWindowTokens);
  const contextUsedPercent = contextWindowTokens > 0
    ? Math.min(100, Math.round((Number(tokens.mainContextTokens) / contextWindowTokens) * 100))
    : 0;

  return (
    <div className="usage-hover-card">
      <button className="usage-trigger" type="button" aria-label="Show token usage">
        <Gauge size={14} />
        <span>{formatTokens(tokens.mainContextTokens)}</span>
        <small>{contextUsedPercent}% ctx</small>
      </button>
      <div className="usage-popout" role="tooltip">
        <div className="usage-popout-header">
          <span>Usage</span>
          <strong>{formatTokens(tokens.mainContextTokens)} / {formatTokens(tokens.contextWindowTokens)}</strong>
        </div>
        <div className="usage-meter" aria-hidden="true">
          <span style={{ width: `${contextUsedPercent}%` }} />
        </div>
        <dl className="usage-summary-grid">
          <div>
            <dt>Context</dt>
            <dd>{formatTokens(tokens.mainContextTokens)}</dd>
          </div>
          <div>
            <dt>Until compact</dt>
            <dd>{formatTokens(tokens.remainingUntilCompactionTokens)}</dd>
          </div>
        </dl>
        <div className="usage-provider-list">
          <ProviderUsageRow label="Codex" usage={codexUsage} showRequests={false} />
          <ProviderUsageRow label="Gemini" usage={geminiUsage} showRequests />
        </div>
        <div className="usage-source">{tokens.source}</div>
      </div>
    </div>
  );
}

function ProviderUsageRow({ label, usage, showRequests }: { label: string; usage?: TokenUsageBreakdown; showRequests: boolean }) {
  return (
    <div className="usage-provider-row">
      <span>{label}</span>
      <strong>{formatTokens(usage?.totalTokens ?? 0)} tokens</strong>
      {showRequests && <small>{formatTokens(usage?.requestCount ?? 0)} requests</small>}
      {!showRequests && <small>{usage?.source ?? "estimate"}</small>}
    </div>
  );
}

function getProviderUsage(tokenUsage: TokenUsageBreakdown[], provider: string) {
  return tokenUsage.find((x) => x.provider.toLowerCase() === provider.toLowerCase());
}

function shorten(value: string, length: number) {
  return value.length <= length ? value : `${value.slice(0, length)}...`;
}

function formatTokens(value: number | string) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(Number(value));
}
