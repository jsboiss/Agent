# Agent

Structure scaffold for the C# Agent described in `PLAN_Agent.md`.

## Project

- `src/Agent`: Blazor Server application with the dashboard, dispatcher contracts, memory placeholders, chat adapters, and Claude bridge boundary organized as folders.
- `tools/ClaudeCodeBridge`: minimal TypeScript bridge boundary for future Claude Code execution.

## Build

```powershell
dotnet build Agent.slnx
```
