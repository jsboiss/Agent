import { useQueryClient } from "@tanstack/react-query";
import { FormEvent, KeyboardEvent, useEffect, useMemo, useRef, useState } from "react";
import { SendHorizontal, Terminal } from "lucide-react";
import { getGetMainChatQueryKey, getMainChatResponse, sendMainChatMessage, useGetMainChat } from "../../api/generated";
import { EmptyState, ErrorState, formatLocalTime, IconButton, LoadingState, StatusChip, TopBar } from "../components";

export function ChatPage() {
  const queryClient = useQueryClient();
  const chatQuery = useGetMainChat();
  const [prompt, setPrompt] = useState("");
  const [pendingPrompt, setPendingPrompt] = useState<string | null>(null);
  const [streamedText, setStreamedText] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const transcriptEndRef = useRef<HTMLDivElement | null>(null);
  const snapshot = chatQuery.data?.data;
  const messages = useMemo(() => {
    const loaded = snapshot?.messages ?? [];
    const optimistic = [...loaded];

    if (pendingPrompt) {
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

  useEffect(() => {
    transcriptEndRef.current?.scrollIntoView({ block: "end", behavior: "smooth" });
  }, [messages.length, streamedText, isStreaming, errorMessage]);

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
            </>
          )}
        />

        <div className="transcript">
          {chatQuery.isLoading && <LoadingState />}
          {chatQuery.isError && <ErrorState error={chatQuery.error} />}
          {!chatQuery.isLoading && !chatQuery.isError && messages.length === 0 && (
            <EmptyState title="No messages yet" body="Start the main local-web conversation." />
          )}
          {messages.map((message) => (
            <article className={`message ${message.role === "You" ? "from-user" : "from-agent"}`} key={message.id}>
              <header>
                <span className="avatar-square">{message.role === "You" ? "Y" : "AI"}</span>
                <strong>{message.role}</strong>
                <time>{formatLocalTime(message.createdAt)}</time>
              </header>
              <MessageBody content={message.content} htmlContent={message.htmlContent} />
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
          <div ref={transcriptEndRef} />
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

function MessageBody({ content, htmlContent }: { content: string; htmlContent: string }) {
  if (htmlContent && htmlContent !== content) {
    return <div className="message-body markdown-body" dangerouslySetInnerHTML={{ __html: htmlContent }} />;
  }

  return <p className="message-body">{content}</p>;
}
