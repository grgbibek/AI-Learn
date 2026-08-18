# Agent Run Artifacts

Each orchestration run writes its outputs to `.agents/runs/<run-id>/` so the loop is **debuggable** and **resumable**. `<run-id>` is an ISO-8601 timestamp with `:` and `.` replaced by `-` (e.g. `2026-08-17T17-30-00Z`).

## Why this exists

- A long agent loop without artifacts is a black box. When something fails at step 4, you need to see what step 3 actually produced.
- Resumability: if a run dies mid-loop, you can restart it from the last successful artifact instead of re-running the planner.
- Cross-agent handoffs: the reviewer doesn't need to re-read the spec; it reads `spec.md`. The debugger doesn't need to re-read the build report; it reads `build-report.md`.

## Layout

```
.agents/runs/<run-id>/
├── spec.md                 # From Feature Orchestrator
├── build-report.md         # From Implementation Agent
├── review-findings.json    # From Code Review Agent
├── test-plan.md            # From Test Strategist
├── debug-diagnosis.md      # From RAG Debugger (only on critical findings)
├── loop-<n>.log            # Optional: per-loop stdout/stderr
└── summary.md              # Final orchestration summary (verdict + files changed)
```

## Artifact contracts

| File | Producer | Required sections | Notes |
| :--- | :--- | :--- | :--- |
| `spec.md` | Feature Orchestrator | `## Goal`, `## Scope`, `## Architecture Plan`, `## API Contract`, `## Angular State & UX`, `## Data / Persistence`, `## Security & Guardrails`, `## Task Breakdown`, `## Validation Plan`, `## Open Questions`, `## Implementation Approval` | `## Implementation Approval` is the gate; downstream agents won't run until it's populated. |
| `build-report.md` | Implementation Agent | `## Implemented`, `## Validation`, `## Files Changed`, `## Remaining Risks / Next Steps` | Paste exact stdout/stderr from build commands under `## Validation`. |
| `review-findings.json` | Code Review Agent | `{ "findings": [{ "severity": "critical\|warning\|nit", "file": "...", "line": 42, "description": "..." }] }` | JSON, not markdown — the orchestrator parses this to decide if Debug should run. |
| `test-plan.md` | Test Strategist | `## Behavior Under Test`, `## Risk Map`, `## Backend Tests`, `## Frontend Tests`, `## Integration / Smoke Tests`, `## Suggested Commands`, `## What Not To Test Yet` | The orchestrator only acts on a "critical" marker in `## Risk Map` or `## Suggested Commands`. |
| `debug-diagnosis.md` | RAG Debugger | `## Symptom`, `## Suspected Path`, `## Evidence To Collect`, `## Likely Causes`, `## Minimal Diagnostic Steps`, `## Recommended Fix`, `## Validation` | Must name **one** stage to loop back to. If it names two, escalate. |
| `summary.md` | Orchestrator (this workflow) | `## Run`, `## Loops`, `## Verdict`, `## Critical Findings`, `## Files Changed`, `## Recommended Next Step` | Human-readable; written once at the end. |

## Critical-finding detection

The orchestrator treats the following as critical (case-insensitive, negative-lookahead for "no critical findings"):

```
critical | blocker | blocking issue | must-fix | fatal
| build failed | test failed | security risk
```

If a stage output contains any of these, the orchestrator routes to the next stage in the loop. If neither reviewer nor tester produces a critical, the loop ends with `verdict: ready-for-human-review`.

## How to resume a partial run

1. Find the latest `<run-id>` directory under `.agents/runs/`.
2. Identify the last artifact produced.
3. Re-invoke the workflow with the same goal and the next stage's role. The orchestrator will pick up from the latest artifact and continue.

## Retention

Runs are kept indefinitely in the working tree. Before committing, decide which runs to keep in git. A reasonable rule: keep `summary.md` for completed runs, gitignore the rest with a top-level `.gitignore` entry like `/.agents/runs/*/loop-*.log`.
