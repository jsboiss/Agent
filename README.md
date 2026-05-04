# Agent

Structure scaffold for the C# Agent described in `PLAN_Agent.md`.

## Project

- `src/Agent`: Blazor Server application with the dashboard, message contracts, memory placeholders, chat channels, and subscription-backed provider contracts.
- `tools/ClaudeCode`: minimal TypeScript Claude Code provider boundary for Claude Agent SDK execution through Claude subscription auth.
- `tools/Codex`: minimal TypeScript Codex provider boundary for Codex CLI execution through ChatGPT subscription auth.

## Build

```powershell
dotnet build Agent.slnx
```

## Claude Code Provider Auth

The C# host does not authenticate to Claude directly. It starts the local
TypeScript provider in `tools/ClaudeCode` over JSON stdio, and that provider
uses the Claude Agent SDK.

For subscription-backed usage, authenticate once with the Claude Code CLI:

```powershell
npm install -g @anthropic-ai/claude-code
claude
```

Sign in when prompted, then exit the CLI. The Claude Agent SDK reuses the
credentials Claude Code saves on disk.

The C# provider removes `ANTHROPIC_API_KEY` from the child process environment
so local subscription usage does not accidentally fall back to API-key billing.
If the SDK reports missing credentials, run `claude` once and sign in again.
Only set `ANTHROPIC_API_KEY` intentionally when you want API-key billing.

Before running the C# host against the Claude provider, build the TypeScript
entrypoint:

```powershell
cd tools/ClaudeCode
npm install
npm run build
```
