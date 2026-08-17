# Agent Workflow Orchestration Implementation Plan

## Goal

Create a GitHub Copilot-friendly orchestration setup that gives this workspace a Claude Code-style workflow experience: named workflow prompts, repeatable specialist roles, project memory through files, and clear verification loops.

This implementation should not build a new runtime AI feature first. It should make the developer workflow itself more agentic and explicit.

## What We Are Building

A set of reusable Copilot prompt/workflow files that guide the agent through common orchestration patterns:

1. Feature orchestration
2. Code review
3. Test strategy
4. RAG/debugging workflow
5. Agentic implementation workflow

These prompts should use the existing workspace rules:

- `AGENTS.md`
- `.github/copilot-instructions.md`
- `.github/instructions/*.instructions.md`
- `.agents/skills/code-review/SKILL.md`
- `.agents/skills/ai-feature-spec/SKILL.md`
- `AI_ROADMAP.md`

## Why This Matters

The user is trying to learn agent/workflow orchestration, not just add another AI feature.

The important learning target is:

```text
Goal -> Plan -> Execute -> Observe -> Verify -> Review -> Finish
```

The implementation should make that workflow visible and repeatable inside VS Code/Copilot.

## Proposed Files

Create prompt files under:

```text
.github/prompts/
```

Suggested files:

```text
.github/prompts/feature-orchestrator.prompt.md
.github/prompts/implementation-agent.prompt.md
.github/prompts/code-review-agent.prompt.md
.github/prompts/test-strategist.prompt.md
.github/prompts/rag-debugger.prompt.md
```

## File 1: feature-orchestrator.prompt.md

Purpose: Take a vague feature idea and turn it into a structured implementation-ready plan.

Workflow:

1. Read `AGENTS.md` and relevant roadmap context.
2. Restate the user goal in concrete terms.
3. Identify affected backend/frontend/data/test surfaces.
4. Produce a technical specification.
5. Produce a task breakdown.
6. Identify risks, unknowns, and validation steps.
7. Stop before editing unless the user explicitly asks to implement.

Should reference the `ai-feature-spec` skill when appropriate.

## File 2: implementation-agent.prompt.md

Purpose: Implement an approved plan with tight scope and verification.

Workflow:

1. Identify the controlling code path.
2. Read only the nearby files needed.
3. Make a small grounded edit.
4. Run the narrowest useful validation.
5. Fix local failures.
6. Continue in small increments.
7. End with build/test/API verification and a concise summary.

This should reinforce the project preference: teach/explain first when learning, but implement directly when the user has explicitly approved implementation.

## File 3: code-review-agent.prompt.md

Purpose: Review changed files like a senior .NET 10 + Angular 19 reviewer.

Workflow:

1. Inspect changed files.
2. Check .NET Minimal API conventions.
3. Check async EF Core usage.
4. Check Angular Standalone Components and Signals.
5. Check security, auth, data sanitization, prompt injection, token budgets, and rate limits.
6. Check streaming cancellation and memory leaks.
7. Check missing tests and verification gaps.
8. Report findings first, ordered by severity.

Should reference `.agents/skills/code-review/SKILL.md`.

## File 4: test-strategist.prompt.md

Purpose: Generate focused tests and verification plans for a changed feature.

Workflow:

1. Identify the behavior under test.
2. Identify backend unit/integration test candidates.
3. Identify Angular component/service tests.
4. Identify runtime smoke tests.
5. Prefer tests that falsify the riskiest assumption.
6. Avoid broad test generation that does not protect behavior.

## File 5: rag-debugger.prompt.md

Purpose: Debug AI/RAG behavior with retrieval, embeddings, prompt, and telemetry evidence.

Workflow:

1. Identify the question or failed answer.
2. Inspect retrieval results and source attribution.
3. Compare vector, keyword, and fused ranking signals.
4. Check sanitization and prompt guard behavior.
5. Check Ollama/model availability.
6. Use telemetry traces when available.
7. Recommend the smallest retrieval/prompt/test change.

## Optional Custom Chat Modes

If VS Code supports custom chat modes in this environment, add modes later for:

```text
Feature Orchestrator
Implementation Agent
Code Reviewer
Test Strategist
RAG Debugger
```

Do not add mode files until the prompt files are working and the user understands the workflow.

## Acceptance Criteria

- Prompt files exist under `.github/prompts/`.
- Each prompt describes a clear role, workflow, inputs, outputs, and stop conditions.
- Prompts are specific to this .NET 10 + Angular 19 + AI-Learn workspace.
- Prompts reference existing project memory instead of duplicating all rules.
- No prompt encourages unsafe autonomous writes without user approval.
- The user can invoke a workflow by asking Copilot Chat to use one of the prompt files.

## Verification

Run lightweight verification:

```powershell
git status --short
```

If prompt files are the only change, no build is required.

Optionally test by asking Copilot Chat:

```text
Use the feature-orchestrator prompt to plan conversation history for the Streaming AI Assistant. Do not implement.
```

Expected result: the agent produces a structured plan and stops before editing.

## Next Step After Prompt Files

Use the `feature-orchestrator` prompt to design the next real orchestration feature:

```text
Agentic Feature Planning Workflow
```

That app feature should later demonstrate runtime orchestration:

```text
feature idea -> spec -> tasks -> test plan -> review checklist -> human approval
```
