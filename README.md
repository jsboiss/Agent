# Agent

Structure scaffold for the C# Agent described in `PLAN_Agent.md`.

## Project

- `src/Agent`: Blazor Server application with the dashboard, pipeline contracts, memory placeholders, chat channels, and Claude boundary organized as folders.
- `tools/Claude`: minimal TypeScript Claude boundary for future Claude Code execution.

## Build

```powershell
dotnet build Agent.slnx
```
