# Meows Guide 2

This guide explains the tools available to this agent in the MainAgent local dashboard and how sub-agent handling works.

## Available tools

### Shell command runner

- Runs PowerShell commands in the configured workspace.
- Use it to inspect files, run builds or tests, create files, and verify repository state.
- Commands run from the requested working directory unless another directory is specified.
- This environment has full filesystem access and network access, but the approval policy is `never`, so work must be completed without asking for elevated command approval.

### Plan updater

- Maintains a concise task plan for multi-step coding work.
- At most one plan item should be marked `in_progress` at a time.
- Use it to show progress when the task has multiple meaningful steps.

### Patch editor

- Applies structured patches to repository files.
- Use it for precise edits when patching is clearer than generating a file with a script.
- Do not run it in parallel with other tools.

### Memory tools

- `search_memory` checks durable agent memory for relevant prior context.
- `write_memory` stores stable user preferences, corrections, identity facts, project notes, or other reusable context when the user provides information worth preserving.
- Memory should not be used for transient implementation details that are only useful for the current turn.

### GitHub app tools

- The connected GitHub app can inspect repositories, issues, pull requests, commits, checks, reviews, workflow runs, and comments.
- It can also create or update issues and pull requests when the user explicitly requests that workflow.
- Repository pushes must not be performed unless the user explicitly asks for a push.

### OpenAI documentation tools

- The OpenAI developer documentation tools search and fetch current official OpenAI docs and endpoint specifications.
- Use them when answering questions about OpenAI APIs, SDKs, models, or product behavior so guidance is current and sourced from official docs.

### Web, image, and file-viewing tools

- Web tools can search or open internet sources when current or externally sourced information is required.
- Image generation tools can create or edit bitmap images when the user asks for visual output.
- Local image viewing can inspect image files that already exist on disk.

### Skill and plugin workflows

- Skills provide task-specific instructions stored in `SKILL.md` files.
- If a user names a skill, or the request clearly matches a skill description, read the skill instructions and follow the relevant workflow.
- Enabled plugins expose related skills, app tools, and MCP capabilities; use those capabilities when the user request matches the plugin domain.

## Sub-agent handling and delegation

Sub-agents are separate child conversations that can perform delegated work. They are useful for parallel, bounded tasks, but they should only be used when delegation is explicitly allowed or requested.

### When to use a sub-agent

- Use a sub-agent only when the user explicitly asks for delegation, sub-agents, parallel agents, or background agent work.
- Delegate concrete, self-contained work that can run independently from the main task.
- Prefer delegation for sidecar research, isolated code changes with a clear ownership boundary, or verification that can run while the main agent does non-overlapping work.

### When not to use a sub-agent

- Do not delegate when the user says to complete the request directly or says not to use a sub-agent.
- Do not delegate urgent blocking work if the main task cannot proceed without the result.
- Do not use sub-agents just because a task is large, detailed, or requires careful codebase analysis.

### How to delegate safely

- Give the sub-agent a self-contained task with all relevant constraints.
- For coding tasks, define ownership of files or modules so parallel agents do not conflict.
- Tell workers that other edits may exist and that they must not revert unrelated changes.
- After a sub-agent finishes, review its summary and any changed files before integrating the result.

### Current request behavior

For this file creation request, the user explicitly instructed the agent not to delegate. Therefore, this guide was created directly in the main workspace without spawning a sub-agent.

## Repository conventions to remember

- Preserve CRLF line endings for Markdown files in this Windows repository unless `.gitattributes` says otherwise.
- After editing files, run `git ls-files --eol | Select-String "w/mixed|i/mixed"` and normalize any touched file that reports mixed endings.
- Use braces for `if`, `foreach`, and `while` statements in code edits.
- Do not suffix async method names with `Async`.
- Prefer properties over fields when stored state is necessary.
- Do not push changes to a remote unless the user explicitly asks.
