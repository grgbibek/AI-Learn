import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const meta = {
  name: 'feature-flow',
  description: 'DAG of Planner -> Implementer -> (Reviewer || Tester) -> conditional RAG Debugger with per-run artifacts under .agents/runs/<run-id>/',
  whenToUse: 'Use when you want one workflow to drive the repo-level agent prompts in a DAG, write per-run artifacts, run reviewer/tester in parallel, and only invoke the RAG Debugger when the failure is retrieval-related.',
  phases: [
    { title: 'Discover', detail: 'List prompts, skills, custom agents, and workflows available in this repo' },
    { title: 'Plan', detail: 'Feature Orchestrator writes spec.md into the run directory' },
    { title: 'Build', detail: 'Implementation Agent writes build-report.md with diff + validation' },
    { title: 'Review || Test', detail: 'Code Review Agent and Test Strategist run in parallel; both write into the run directory' },
    { title: 'Debug (conditional)', detail: 'RAG Debugger only when reviewer/tester critical finding is RAG-related or build failed in a retrieval path' },
    { title: 'Finalize', detail: 'Write summary.md, the in-place last-report.md/json, and return a verdict' },
  ],
};

const __dirname = dirname(fileURLToPath(import.meta.url));
const WORKSPACE_ROOT = findWorkspaceRoot(__dirname);
const ROLE_FILES = {
  planner: '.github/prompts/feature-orchestrator.prompt.md',
  developer: '.github/prompts/implementation-agent.prompt.md',
  reviewer: '.github/prompts/code-review-agent.prompt.md',
  tester: '.github/prompts/test-strategist.prompt.md',
  debugger: '.github/prompts/rag-debugger.prompt.md',
};
const FALLBACK_PROMPTS = {
  planner: 'Act as the Feature Orchestrator. Produce a scoped technical spec, task breakdown, risks, and validation plan. Do not edit code.',
  developer: 'Act as the Implementation Agent. Implement the approved spec in small validated edits and report files changed plus validation results.',
  reviewer: 'Act as the Code Review Agent. Review changed files for bugs, security risks, architecture drift, memory leaks, and missing tests. Findings first.',
  tester: 'Act as the Test Strategist. Design focused backend, frontend, integration, and smoke tests for the changed behavior.',
  debugger: 'Act as the RAG Debugger. Use evidence to identify the failing stage in retrieval/embedding/prompt/generation and propose the smallest targeted fix plus validation command.',
};

const workflowAgent = typeof agent === 'function' ? agent : null;
const workflowPhase = typeof phase === 'function' ? phase : null;
const workflowArgs = typeof args === 'string' ? args : process.argv.slice(2).join(' ');
const cliMode = !workflowAgent;

const result = await main(workflowArgs);

if (cliMode) {
  console.log(JSON.stringify(result, null, 2));
}

export default result;

async function main(rawArgs) {
  const options = parseOptions(rawArgs);
  const discoveredAgents = discoverAgents();
  const report = createRunReport(options, discoveredAgents);
  const runId = report.runId;
  const runDir = join(WORKSPACE_ROOT, '.agents/runs', runId);
  mkdirSync(runDir, { recursive: true });
  report.runDir = `.agents/runs/${runId}`;

  writePhase('Discover');
  writeLog(`Discovered ${discoveredAgents.length} agent-like definitions.`);
  for (const item of discoveredAgents) {
    writeLog(`- ${item.type}: ${item.name} (${item.path})`);
  }

  if (options.listOnly || !options.goal) {
    return finalizeRun({
      mode: cliMode ? 'node-ollama' : 'workflow-runtime',
      verdict: options.listOnly ? 'listed-agents-only' : 'missing-goal',
      agents: discoveredAgents,
      runDir: report.runDir,
      usage: 'Run with a feature goal, or pass --list-agents to list available agents without running the loop.',
    }, report, options);
  }

  writePhase('Plan');
  const spec = await runRole('planner', buildPlannerInput(options.goal, runId), { phase: 'Plan', effort: 'medium' }, report, options);
  if (!spec) {
    return finalizeRun({ verdict: 'aborted', reason: 'planner returned no output', agents: discoveredAgents, runDir: report.runDir }, report, options);
  }
  const specPath = join(runDir, 'spec.md');
  writeFileSync(specPath, spec, 'utf8');
  report.artifacts.spec = pathFromRoot(specPath);

  // Gate: the orchestrator must populate "## Implementation Approval" before the loop runs.
  // --approve-spec is a local trusted override for CLI runs (e.g. a developer who has reviewed
  // the spec out-of-band and wants the loop to proceed without editing spec.md).
  if (options.approveSpec) {
    const approvedBlock = `\n\n## Implementation Approval\n\nApproved (auto-approved via --approve-spec for local CLI run at ${new Date().toISOString()}).\n`;
    if (!hasImplementationApproval(spec)) {
      const augmented = spec.replace(/\s*$/, '') + approvedBlock;
      writeFileSync(join(runDir, 'spec.md'), augmented, 'utf8');
      report.artifacts.spec = pathFromRoot(join(runDir, 'spec.md'));
      report.specAutoApproved = true;
    }
  }

  if (!hasImplementationApproval(spec)) {
    return finalizeRun({
      verdict: 'awaiting-human-approval',
      reason: 'spec.md does not contain "## Implementation Approval" — a human must approve before the Implementation Agent runs',
      agents: discoveredAgents,
      runDir: report.runDir,
      specPath: report.artifacts.spec,
    }, report, options);
  }

  let buildReport = '';
  let reviewReport = '';
  let testReport = '';
  let debugReport = '';
  let loop = 0;
  let currentSpec = spec;

  while (loop < options.maxLoops) {
    loop += 1;
    const loopReport = { loop, startedAt: new Date().toISOString() };
    report.loops.push(loopReport);

    writePhase(`Build ${loop}`);
    buildReport = await runRole('developer', buildDeveloperInput(currentSpec, debugReport, runDir), {
      phase: 'Build',
      effort: 'high',
    }, report, options);
    if (!buildReport) {
      loopReport.finishedAt = new Date().toISOString();
      loopReport.verdict = 'developer-returned-no-output';
      return finalizeRun({ verdict: 'aborted', reason: 'developer returned no output', agents: discoveredAgents, runDir: report.runDir, specPath: report.artifacts.spec }, report, options);
    }
    const buildReportPath = join(runDir, 'build-report.md');
    writeFileSync(buildReportPath, buildReport, 'utf8');
    report.artifacts.buildReport = pathFromRoot(buildReportPath);

    // Gate: build must have passed (or the run is RAG-related and the failure is acceptable)
    if (buildFailed(buildReport) && !looksRagRelated(buildReport)) {
      loopReport.buildGate = 'failed-non-rag';
      return finalizeRun({
        verdict: 'build-failed-escalate',
        reason: 'Implementation Agent reported a non-RAG build failure; human review required',
        agents: discoveredAgents,
        runDir: report.runDir,
        specPath: report.artifacts.spec,
        buildReportPath: report.artifacts.buildReport,
      }, report, options);
    }

    writePhase(`Review || Test ${loop}`);
    // Parallel where the runtime supports it; sequential fallback in CLI/Ollama mode.
    const [reviewResult, testResult] = options.mockAgents
      ? [await runRole('reviewer', buildReviewerInput(currentSpec, buildReport, runDir), { phase: 'Review', effort: 'high' }, report, options),
         await runRole('tester', buildTesterInput(currentSpec, buildReport, '', runDir), { phase: 'Test', effort: 'medium' }, report, options)]
      : workflowAgent
        ? await Promise.all([
            runRole('reviewer', buildReviewerInput(currentSpec, buildReport, runDir), { phase: 'Review', effort: 'high' }, report, options),
            runRole('tester', buildTesterInput(currentSpec, buildReport, '', runDir), { phase: 'Test', effort: 'medium' }, report, options),
          ])
        : [await runRole('reviewer', buildReviewerInput(currentSpec, buildReport, runDir), { phase: 'Review', effort: 'high' }, report, options),
           await runRole('tester', buildTesterInput(currentSpec, buildReport, '', runDir), { phase: 'Test', effort: 'medium' }, report, options)];

    reviewReport = reviewResult;
    testReport = testResult;

    // Now run the tester a second time with the reviewer's findings in scope, so test gaps include review findings.
    if (!options.mockAgents && reviewReport) {
      testReport = await runRole('tester', buildTesterInput(currentSpec, buildReport, reviewReport, runDir), { phase: 'Test', effort: 'medium' }, report, options);
    }

    const reviewPath = join(runDir, 'review-findings.json');
    const testPath = join(runDir, 'test-plan.md');
    writeFileSync(reviewPath, reviewReport || '', 'utf8');
    writeFileSync(testPath, testReport || '', 'utf8');
    report.artifacts.reviewFindings = pathFromRoot(reviewPath);
    report.artifacts.testPlan = pathFromRoot(testPath);

    loopReport.reviewCritical = hasCritical(reviewReport);
    loopReport.testCritical = hasCritical(testReport);
    loopReport.reviewRag = looksRagRelated(reviewReport);
    loopReport.testRag = looksRagRelated(testReport);
    loopReport.finishedAt = new Date().toISOString();

    if (!loopReport.reviewCritical && !loopReport.testCritical) {
      loopReport.verdict = 'ready-for-human-review';
      return finalizeRun({
        mode: cliMode ? 'node-ollama' : 'workflow-runtime',
        verdict: 'ready-for-human-review',
        agents: discoveredAgents,
        runDir: report.runDir,
        specPath: report.artifacts.spec,
        buildReportPath: report.artifacts.buildReport,
        reviewPath: report.artifacts.reviewFindings,
        testPath: report.artifacts.testPlan,
        loops: loop,
      }, report, options);
    }

    // Conditional: only invoke the RAG Debugger when the critical finding is RAG-related.
    const ragSymptom = loopReport.reviewRag || loopReport.testRag || looksRagRelated(buildReport);
    if (!ragSymptom) {
      loopReport.verdict = 'critical-non-rag-escalate';
      return finalizeRun({
        verdict: 'critical-non-rag-escalate',
        reason: 'Reviewer/tester reported a critical finding that is not RAG-related; human review required',
        agents: discoveredAgents,
        runDir: report.runDir,
        specPath: report.artifacts.spec,
        buildReportPath: report.artifacts.buildReport,
        reviewPath: report.artifacts.reviewFindings,
        testPath: report.artifacts.testPlan,
        loops: loop,
      }, report, options);
    }

    writePhase(`Debug ${loop}`);
    loopReport.debugRouted = true;
    debugReport = await runRole('debugger', buildDebuggerInput(currentSpec, buildReport, reviewReport, testReport, runDir), {
      phase: 'Debug',
      effort: 'high',
    }, report, options);
    const debugPath = join(runDir, 'debug-diagnosis.md');
    writeFileSync(debugPath, debugReport || '', 'utf8');
    report.artifacts.debugDiagnosis = pathFromRoot(debugPath);

    currentSpec = `${spec}\n\n## Debug Context From Loop ${loop}\n\n${debugReport}\n\nArtifacts: ${report.artifacts.debugDiagnosis}`;
  }

  return finalizeRun({
    mode: cliMode ? 'node-ollama' : 'workflow-runtime',
    verdict: 'max-loops-reached-needs-human-review',
    agents: discoveredAgents,
    runDir: report.runDir,
    specPath: report.artifacts.spec,
    buildReportPath: report.artifacts.buildReport,
    reviewPath: report.artifacts.reviewFindings,
    testPath: report.artifacts.testPlan,
    debugPath: report.artifacts.debugDiagnosis,
    loops: options.maxLoops,
  }, report, options);
}

async function runRole(role, input, roleOptions, report, runOptions) {
  const rolePrompt = loadRolePrompt(role);
  const prompt = `${rolePrompt}\n\n---\nORCHESTRATOR INPUT\n${input}\n\n---\nReturn your output in the format requested by your role prompt.`;

  writeLog(`Running ${role} agent...`);
  const stage = {
    role,
    phase: roleOptions.phase,
    effort: roleOptions.effort,
    startedAt: new Date().toISOString(),
    inputChars: input.length,
  };
  const started = performance.now();

  try {
    const output = runOptions.mockAgents
      ? mockRoleOutput(role, input)
      : workflowAgent
        ? await workflowAgent(prompt, {
            label: role,
            phase: roleOptions.phase,
            agentType: 'general-purpose',
            effort: roleOptions.effort,
          })
        : await runWithOllama(role, prompt, runOptions.agentTimeoutMs);

    stage.finishedAt = new Date().toISOString();
    stage.durationMs = Math.round(performance.now() - started);
    stage.outputChars = String(output || '').length;
    stage.critical = hasCritical(output);
    stage.ragRelated = looksRagRelated(output);
    stage.feedbackSummary = summarizeText(output);
    report.stages.push(stage);
    writeLog(`${role} finished in ${formatDuration(stage.durationMs)} (${stage.outputChars} chars, critical=${stage.critical}, rag=${stage.ragRelated}).`);

    return output;
  } catch (error) {
    stage.finishedAt = new Date().toISOString();
    stage.durationMs = Math.round(performance.now() - started);
    stage.error = error instanceof Error ? error.message : String(error);
    stage.critical = true;
    stage.ragRelated = false;
    stage.feedbackSummary = stage.error;
    report.stages.push(stage);
    throw error;
  }
}

function buildPlannerInput(goal, runId) {
  return `USER GOAL:\n${goal}\n\nRUN ID: ${runId}\nRUN DIRECTORY: .agents/runs/${runId}\n\nWrite the spec to .agents/runs/${runId}/spec.md using the format in your role prompt. Set "## Implementation Approval" only when a human or trusted orchestrator has approved the plan; if not yet approved, leave that section empty so the loop stops.`;
}

function buildDeveloperInput(spec, debugReport, runDir) {
  return `APPROVED SPEC FROM PLANNER:\n${spec}\n\n${debugReport ? `DEBUG FEEDBACK TO APPLY:\n${debugReport}\n\n` : ''}RUN DIRECTORY: ${runDir}\n\nImplement the smallest useful slice, validate it locally, and report changed files plus validation results. Write your full report to ${runDir}/build-report.md in the format your role prompt requires.`;
}

function buildReviewerInput(spec, buildReport, runDir) {
  return `APPROVED SPEC:\n${spec}\n\nBUILD / IMPLEMENTATION REPORT:\n${buildReport}\n\nRUN DIRECTORY: ${runDir}\n\nReview the resulting changes and write findings to ${runDir}/review-findings.json in the format your role prompt requires. If the change is RAG-related (retrieval, embedding, prompt, generation, streaming, telemetry), say so explicitly so the orchestrator can route the debug step.`;
}

function buildTesterInput(spec, buildReport, reviewReport, runDir) {
  return `APPROVED SPEC:\n${spec}\n\nBUILD / IMPLEMENTATION REPORT:\n${buildReport}\n\nREVIEW REPORT:\n${reviewReport || '(reviewer returned no output)'}\n\nRUN DIRECTORY: ${runDir}\n\nDesign or run focused validation for the highest-risk behavior and write your plan to ${runDir}/test-plan.md in the format your role prompt requires. If a test gap is RAG-related, say so explicitly.`;
}

function buildDebuggerInput(spec, buildReport, reviewReport, testReport, runDir) {
  return `APPROVED SPEC:\n${spec}\n\nBUILD REPORT:\n${buildReport}\n\nREVIEW REPORT:\n${reviewReport}\n\nTEST REPORT:\n${testReport}\n\nRUN DIRECTORY: ${runDir}\n\nA RAG-related critical/blocking issue was detected. Diagnose the failing stage (ingestion, embedding, retrieval, reranking, prompt construction, generation, streaming, or UI rendering) and write the smallest fix plus validation command to ${runDir}/debug-diagnosis.md. Name exactly one stage to loop back to; if ambiguous, say so and stop.`;
}

function discoverAgents() {
  return [
    ...discoverPromptFiles('.github/prompts', 'prompt'),
    ...discoverPromptFiles('.github/agents', 'custom-agent'),
    ...discoverPromptFiles('.claude/agents', 'claude-agent'),
    ...discoverSkills('.agents/skills'),
    ...discoverPromptFiles('.claude/workflows', 'workflow'),
  ].sort((a, b) => `${a.type}:${a.name}`.localeCompare(`${b.type}:${b.name}`));
}

function discoverPromptFiles(relativeDir, type) {
  const directory = join(WORKSPACE_ROOT, relativeDir);
  if (!existsSync(directory)) return [];

  return readdirSync(directory, { withFileTypes: true })
    .filter(entry => entry.isFile())
    .filter(entry => entry.name.endsWith('.md') || entry.name.endsWith('.js'))
    .map(entry => {
      const relativePath = `${relativeDir}/${entry.name}`.replaceAll('\\', '/');
      const content = safeRead(relativePath);
      const frontmatter = parseFrontmatter(content);
      return {
        type,
        name: frontmatter.name || entry.name.replace(/\.(prompt|agent)\.md$|\.md$|\.js$/g, ''),
        description: frontmatter.description || frontmatter.whenToUse || '',
        path: relativePath,
      };
    });
}

function discoverSkills(relativeDir) {
  const directory = join(WORKSPACE_ROOT, relativeDir);
  if (!existsSync(directory)) return [];

  return readdirSync(directory, { withFileTypes: true })
    .filter(entry => entry.isDirectory())
    .map(entry => {
      const relativePath = `${relativeDir}/${entry.name}/SKILL.md`.replaceAll('\\', '/');
      const content = safeRead(relativePath);
      const frontmatter = parseFrontmatter(content);
      return {
        type: 'skill',
        name: frontmatter.name || entry.name,
        description: frontmatter.description || '',
        path: relativePath,
      };
    })
    .filter(item => existsSync(join(WORKSPACE_ROOT, item.path)));
}

function loadRolePrompt(role) {
  const relativePath = ROLE_FILES[role];
  const content = relativePath ? safeRead(relativePath) : '';
  return stripFrontmatter(content).trim() || FALLBACK_PROMPTS[role];
}

async function runWithOllama(role, prompt, timeoutMs) {
  const config = readJson('backend/appsettings.Development.json');
  const baseUrl = process.env.OLLAMA_BASE_URL || config?.Ollama?.BaseUrl || 'http://localhost:11434';
  const model = process.env.OLLAMA_MODEL || config?.Ollama?.ChatModel || 'llama3.2';
  const abortController = new AbortController();
  const timeout = setTimeout(() => abortController.abort(), timeoutMs);

  try {
  const response = await fetch(`${baseUrl.replace(/\/$/, '')}/api/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    signal: abortController.signal,
    body: JSON.stringify({
      model,
      stream: false,
      // Cap output so a stage can't run forever on CPU. 1500 tokens is enough for a tight spec,
      // a build report, a review finding list, or a debug diagnosis.
      options: { num_predict: 1500, num_ctx: 4096 },
      keep_alive: '5m',
      messages: [
        {
          role: 'system',
          content: `You are the ${role} agent in a bounded local orchestrator. You cannot edit files unless an external tool-capable runtime is executing you. Be explicit about what you did versus what you recommend. Keep your output tight: use the headings your role prompt requires, no prose preamble, no recap of the role description.`,
        },
        { role: 'user', content: prompt },
      ],
    }),
  });

  if (!response.ok) {
    throw new Error(`Ollama ${role} call failed with HTTP ${response.status}`);
  }

  const data = await response.json();
  return data?.message?.content ?? '';
  } finally {
    clearTimeout(timeout);
  }
}

function mockRoleOutput(role, input) {
  const inputPreview = summarizeText(input, 240);

  if (role === 'planner') {
    return `## Goal\nMock plan for: ${inputPreview}\n\n## Scope\nA tiny, safe feature slice.\n\n## Task Breakdown\n1. Identify target file.\n2. Make minimal copy or state change.\n3. Validate build.\n\n## Validation Plan\nRun frontend/backend build as applicable.\n\n## Implementation Approval\nApproved for mock flow only.`;
  }

  if (role === 'developer') {
    return `## Implemented\nMock developer received the planner spec and would implement the smallest useful slice.\n\n## Validation\nMock validation passed.\n\n## Files Changed\nNone in mock mode.\n\n## Remaining Risks / Next Steps\nRun without --mock-agents in a tool-capable runtime for real edits.`;
  }

  if (role === 'reviewer') {
    return `## Findings\nNo critical findings in mock mode.\n\n## Open Questions / Assumptions\nAssumes real implementation would preserve Angular Signals and .NET Minimal API patterns.\n\n## Test Gaps\nReal tests not executed in mock mode.\n\n## Summary\nReady for human review of the orchestration report.`;
  }

  if (role === 'tester') {
    return `## Behavior Under Test\nMock orchestration flow and report generation.\n\n## Risk Map\nMain risk is role handoff or report generation failure.\n\n## Suggested Commands\nnode --check .claude/workflows/feature-flow.js\nnode .claude/workflows/feature-flow.js --list-agents\n\n## What Not To Test Yet\nDo not infer real code correctness from mock mode.`;
  }

  return `## Symptom\nMock debug route.\n\n## Suspected Path\nMock retrieval path.\n\n## Recommended Fix\nNo fix required unless a real stage reports a blocker.\n\n## Validation\nRerun with --mock-agents or a real goal.`;
}

function parseOptions(rawArgs) {
  const raw = String(rawArgs || '').trim();
  const maxLoopsMatch = raw.match(/--max-loops=(\d+)/);
  const timeoutMatch = raw.match(/--agent-timeout-ms=(\d+)/);
  const maxLoops = maxLoopsMatch ? Math.max(1, Math.min(5, Number(maxLoopsMatch[1]))) : 2;
  const agentTimeoutMs = timeoutMatch ? Math.max(10_000, Number(timeoutMatch[1])) : 90_000;
  const listOnly = /(^|\s)--list-agents(\s|$)/.test(raw);
  const noReport = /(^|\s)--no-report(\s|$)/.test(raw);
  const mockAgents = /(^|\s)--mock-agents(\s|$)/.test(raw);
  const approveSpec = /(^|\s)--approve-spec(\s|$)/.test(raw);
  const goal = raw
    .replace(/--max-loops=\d+/g, '')
    .replace(/--agent-timeout-ms=\d+/g, '')
    .replace(/--list-agents/g, '')
    .replace(/--no-report/g, '')
    .replace(/--mock-agents/g, '')
    .replace(/--approve-spec/g, '')
    .trim();

  return { goal, listOnly, maxLoops, noReport, mockAgents, approveSpec, agentTimeoutMs };
}

function hasCritical(text) {
  const value = String(text || '').toLowerCase();
  if (!value.trim()) return false;

  if (/(no|none|without)\s+(critical|blocker|blocking|must[- ]fix|fatal|security risk|test failed|build failed)/i.test(value)
    || /no\s+findings/i.test(value)
    || /no\s+critical\s+findings/i.test(value)) {
    return false;
  }

  return /critical\s+finding|blocker|blocking issue|must[- ]fix|fatal|build failed|test failed|security risk/i.test(value);
}

function looksRagRelated(text) {
  const value = String(text || '').toLowerCase();
  if (!value.trim()) return false;

  return /\b(rag|retrieval|retriever|embedding|embeddings|qdrant|kernel memory|semantic kernel|chunking|chunker|rerank|reranker|vector|cosine|hybrid search|bm25|prompt injection|sanitization|streaming assistant|telemetry|ollama|aspiration|aspiration score|top[-_ ]?k|cosine similarity)\b/.test(value);
}

function buildFailed(text) {
  const value = String(text || '').toLowerCase();
  return /build failed|dotnet build.*(fail|error)|npm.*build.*(fail|error)|compilation failed|cs\d{4}:|error cs\d{4}/.test(value);
}

function hasImplementationApproval(spec) {
  const value = String(spec || '');
  const match = value.match(/##\s*Implementation Approval\s*\n([\s\S]*?)(?:\n##\s|\s*$)/i);
  if (!match) return false;
  const body = match[1].toLowerCase();
  return /approved|approval\s*granted|✓|yes/i.test(body) && !/not\s+approved|pending|awaiting|unapproved|deferred/i.test(body);
}

function createRunReport(options, agents) {
  const runId = `feature-flow-${new Date().toISOString().replace(/[:.]/g, '-')}`;
  return {
    runId,
    mode: cliMode ? 'node-ollama' : 'workflow-runtime',
    goal: options.goal || null,
    maxLoops: options.maxLoops,
    startedAt: new Date().toISOString(),
    finishedAt: null,
    durationMs: null,
    verdict: null,
    runDir: null,
    artifacts: {},
    agents,
    stages: [],
    loops: [],
  };
}

function finalizeRun(result, report, options) {
  report.finishedAt = new Date().toISOString();
  report.durationMs = Date.parse(report.finishedAt) - Date.parse(report.startedAt);
  report.verdict = result.verdict;

  // Write the in-place summary report (always), the per-run summary.md, and the JSON.
  if (report.runDir) {
    try {
      const summaryPath = join(WORKSPACE_ROOT, report.runDir, 'summary.md');
      writeFileSync(summaryPath, renderRunSummary(result, report), 'utf8');
      report.artifacts.summary = pathFromRoot(summaryPath);
    } catch (error) {
      report.summaryWriteError = error instanceof Error ? error.message : String(error);
    }
  }

  if (!options.noReport) {
    writeRunReport({ ...result, orchestrationReport: report }, report);
  }

  return {
    ...result,
    orchestrationReport: report,
  };
}

function writeRunReport(result, report) {
  try {
    const reportDir = join(WORKSPACE_ROOT, '.claude/workflows/reports');
    mkdirSync(reportDir, { recursive: true });

    const markdownPath = join(reportDir, 'feature-flow-last-report.md');
    const jsonPath = join(reportDir, 'feature-flow-last-report.json');

    writeFileSync(markdownPath, renderMarkdownReport(result, report), 'utf8');
    writeFileSync(jsonPath, JSON.stringify(result, null, 2), 'utf8');

    report.reportMarkdownPath = '.claude/workflows/reports/feature-flow-last-report.md';
    report.reportJsonPath = '.claude/workflows/reports/feature-flow-last-report.json';
    writeLog(`Report written to ${report.reportMarkdownPath}`);
  } catch (error) {
    report.reportWriteError = error instanceof Error ? error.message : String(error);
    writeLog(`Report write failed: ${report.reportWriteError}`);
  }
}

function renderRunSummary(result, report) {
  const fileList = report.stages
    .filter(stage => stage.role === 'developer' && stage.feedbackSummary)
    .map(stage => stage.feedbackSummary)
    .join('\n');
  const criticals = report.stages
    .filter(stage => stage.critical)
    .map(stage => `- ${stage.role} (${stage.phase || '-'}) — ${summarizeText(stage.feedbackSummary, 200)}`)
    .join('\n') || '- none';

  return `# Orchestration Summary

- **Run ID:** ${report.runId}
- **Run Directory:** ${report.runDir}
- **Goal:** ${report.goal ?? '(none)'}
- **Verdict:** ${result.verdict}
- **Loops Executed:** ${result.loops ?? report.loops.length}
- **Total Duration:** ${formatDuration(report.durationMs)}

## Artifacts

${Object.entries(report.artifacts || {}).map(([key, value]) => `- **${key}**: ${value}`).join('\n') || '- (none yet)'}

## Critical Findings Remaining

${criticals}

## Files Changed (from build-report)

${fileList || 'See build-report.md in the run directory.'}

## Recommended Next Step

${result.verdict === 'ready-for-human-review' ? 'A human should review the diff and merge if the build report and review findings look clean.' :
  result.verdict === 'awaiting-human-approval' ? 'A human must set "## Implementation Approval" in spec.md, then re-run the workflow.' :
  result.verdict === 'critical-non-rag-escalate' ? 'A critical non-RAG finding was reported. Triage the reviewer/tester output and either fix the underlying issue or update the spec.' :
  result.verdict === 'build-failed-escalate' ? 'The Implementation Agent reported a non-RAG build failure. Read build-report.md and fix the compile/lint error before re-running.' :
  'Inspect the run directory, then either refine the spec and re-run, or escalate to a human.'}
`;
}

function renderMarkdownReport(result, report) {
  const stageRows = report.stages.length
    ? report.stages.map(stage => `| ${stage.role} | ${stage.phase} | ${formatDuration(stage.durationMs)} | ${stage.outputChars ?? 0} | ${stage.critical ? 'yes' : 'no'} | ${stage.ragRelated ? 'yes' : 'no'} | ${escapeTable(stage.feedbackSummary)} |`).join('\n')
    : '| none | - | - | - | - | - | - |';
  const loopRows = report.loops.length
    ? report.loops.map(loop => `| ${loop.loop} | ${loop.verdict ?? 'debug-routed'} | ${loop.reviewCritical ? 'yes' : 'no'} | ${loop.testCritical ? 'yes' : 'no'} | ${loop.reviewRag || loop.testRag ? 'yes' : 'no'} | ${loop.debugRouted ? 'yes' : 'no'} |`).join('\n')
    : '| 0 | list-only/no-goal | no | no | no | no |';
  const agents = report.agents.map(agent => `- **${agent.type}**: ${agent.name} (${agent.path})`).join('\n');
  const feedback = report.stages
    .filter(stage => ['reviewer', 'tester', 'debugger'].includes(stage.role))
    .map(stage => `### ${stage.role} feedback\n\n${stage.feedbackSummary || '(no feedback)'}`)
    .join('\n\n') || '(No reviewer/tester/debugger feedback captured.)';
  const artifacts = Object.entries(report.artifacts || {}).map(([key, value]) => `- **${key}**: ${value}`).join('\n') || '- (none)';

  return `# Feature Flow Orchestration Report

## Summary

- **Run ID:** ${report.runId}
- **Mode:** ${report.mode}
- **Run Directory:** ${report.runDir ?? '(not created)'}
- **Goal:** ${report.goal ?? '(none)'}
- **Verdict:** ${report.verdict}
- **Started:** ${report.startedAt}
- **Finished:** ${report.finishedAt}
- **Total Duration:** ${formatDuration(report.durationMs)}
- **Loops Ran:** ${result.loops ?? report.loops.length}

## Available Agents

${agents}

## Artifacts

${artifacts}

## Agent Timings

| Agent | Phase | Duration | Output Chars | Critical? | RAG? | Feedback Summary |
| :--- | :--- | ---: | ---: | :---: | :---: | :--- |
${stageRows}

## Loop Summary

| Loop | Verdict | Review Critical? | Test Critical? | RAG Symptom? | Debug Routed? |
| ---: | :--- | :---: | :---: | :---: | :---: |
${loopRows}

## Feedback

${feedback}
`;
}

function summarizeText(text, maxLength = 500) {
  const normalized = String(text || '')
    .replace(/\r/g, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim();

  if (!normalized) return '(no output)';

  const importantSection = normalized.match(/##\s*(Findings|Validation|Test Gaps|Recommended Fix|Summary|Suspected Path)[\s\S]{0,700}/i)?.[0];
  const summary = importantSection || normalized;

  return summary.length <= maxLength ? summary : `${summary.slice(0, maxLength - 3)}...`;
}

function formatDuration(durationMs) {
  if (durationMs === null || durationMs === undefined || Number.isNaN(durationMs)) return '-';
  if (durationMs < 1000) return `${durationMs}ms`;
  return `${(durationMs / 1000).toFixed(1)}s`;
}

function escapeTable(value) {
  return String(value || '')
    .replace(/\|/g, '\\|')
    .replace(/\r?\n/g, '<br>');
}

function pathFromRoot(absolutePath) {
  return absolutePath.startsWith(WORKSPACE_ROOT)
    ? absolutePath.slice(WORKSPACE_ROOT.length).replace(/^[\\/]+/, '').replaceAll('\\', '/')
    : absolutePath;
}

function parseFrontmatter(content) {
  const match = String(content || '').match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (!match) return {};

  return Object.fromEntries(match[1]
    .split(/\r?\n/)
    .map(line => line.match(/^([A-Za-z0-9_-]+):\s*["']?(.*?)["']?\s*$/))
    .filter(Boolean)
    .map(([, key, value]) => [key, value]));
}

function stripFrontmatter(content) {
  return String(content || '').replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n?/, '');
}

function safeRead(relativePath) {
  const absolutePath = join(WORKSPACE_ROOT, relativePath);
  return existsSync(absolutePath) ? readFileSync(absolutePath, 'utf8') : '';
}

function readJson(relativePath) {
  try {
    const content = safeRead(relativePath);
    return content ? JSON.parse(content) : null;
  } catch {
    return null;
  }
}

function findWorkspaceRoot(startDirectory) {
  let current = startDirectory;
  while (current && current !== dirname(current)) {
    if (existsSync(join(current, 'AGENTS.md'))) return current;
    current = dirname(current);
  }

  return process.cwd();
}

function writePhase(name) {
  if (workflowPhase) workflowPhase(name);
  else console.error(`\n== ${name} ==`);
}

function writeLog(message) {
  if (typeof globalThis.log === 'function') globalThis.log(message);
  else console.error(message);
}
