## Behavior Under Test
Mock orchestration flow and report generation.

## Risk Map
Main risk is role handoff or report generation failure.

## Suggested Commands
node --check .claude/workflows/feature-flow.js
node .claude/workflows/feature-flow.js --list-agents

## What Not To Test Yet
Do not infer real code correctness from mock mode.