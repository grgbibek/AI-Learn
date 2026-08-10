# TaskFlow Local Startup Guide

This guide explains how to start the full TaskFlow learning app locally: backend API, Angular frontend, Ollama models, Aspire Dashboard telemetry, optional Qdrant, and optional MCP server.

## Quick Start

Open separate terminals from the workspace root:

```powershell
# 1. Start Ollama if it is not already running
ollama serve
```

```powershell
# 2. Start Aspire Dashboard for visual OpenTelemetry traces
npx -y @microsoft/aspire-cli dashboard run --allow-anonymous
```

```powershell
# 3. Start the .NET backend API
dotnet run --project backend/backend.csproj
```

```powershell
# 4. Start the Angular frontend
Push-Location frontend
npm start
Pop-Location
```

Then open:

| Tool | URL | Purpose |
|---|---|---|
| Angular app | `http://localhost:4200` | Main TaskFlow UI |
| Backend API docs | `http://localhost:5198/scalar` | Test Minimal API endpoints |
| Aspire Dashboard | `http://localhost:18888` | View OpenTelemetry traces/logs/metrics |
| Ollama API | `http://localhost:11434` | Local LLM API root |

## Prerequisites

Install or have available:

- .NET SDK capable of building `net10.0` projects.
- Node.js and npm.
- Angular CLI dependencies installed through `frontend/package.json`.
- SQL Server LocalDB, using the connection string in `backend/appsettings.Development.json`.
- Ollama with the expected local models.

Pull the Ollama models once:

```powershell
ollama pull llama3.2
ollama pull nomic-embed-text
```

Install frontend dependencies once:

```powershell
Push-Location frontend
npm install
Pop-Location
```

## Local Ports

| Port | Owner | Notes |
|---:|---|---|
| `4200` | Angular dev server | Main frontend app |
| `5198` | ASP.NET Core backend | API and Scalar docs |
| `11434` | Ollama | Chat and embedding model server |
| `18888` | Aspire Dashboard | Browser UI for telemetry |
| `4317` | Aspire Dashboard | OTLP/gRPC receiver, not a browser page |
| `4318` | Aspire Dashboard | OTLP/HTTP receiver, not usually opened directly |
| `6333` | Qdrant optional | REST API if Qdrant is running |
| `6334` | Qdrant optional | gRPC API used by the .NET Qdrant client |

Important distinction:

```text
http://localhost:4317 = telemetry ingestion pipe
http://localhost:18888 = dashboard UI for humans
```

The backend sends traces to `4317`; you view them in the browser at `18888`.

## Backend API

Start the backend:

```powershell
dotnet run --project backend/backend.csproj
```

Or use the VS Code task:

```text
Terminal > Run Task > watch
```

The backend uses:

- `backend/appsettings.Development.json`
- SQL Server LocalDB database `TaskFlowDb`
- Ollama at `http://localhost:11434`
- OTLP export to `http://localhost:4317`

Backend URLs:

| URL | Purpose |
|---|---|
| `http://localhost:5198/` | Redirects to Scalar in Development |
| `http://localhost:5198/scalar` | Interactive API docs |
| `http://localhost:5198/api/workitems/` | Work item CRUD |
| `http://localhost:5198/api/rag/ask` | SQL hybrid RAG ask |
| `http://localhost:5198/api/rag/ask-stream` | Streaming SQL hybrid RAG ask |
| `http://localhost:5198/api/rag/qdrant/ask` | Qdrant RAG comparison |
| `http://localhost:5198/api/rag/kernel-memory/ask` | Kernel Memory comparison |
| `http://localhost:5198/api/agents/plan-feature` | Multi-agent pipeline |
| `http://localhost:5198/api/analytics/metrics` | Dashboard metrics |

## Frontend UI

Start the frontend:

```powershell
Push-Location frontend
npm start
Pop-Location
```

Open:

```text
http://localhost:4200
```

The header includes quick links for:

- Ollama: `http://localhost:11434`
- Aspire Dashboard: `http://localhost:18888`

Build the frontend:

```powershell
Push-Location frontend
npm run build
Pop-Location
```

## Aspire Dashboard And OpenTelemetry

Start the dashboard first:

```powershell
npx -y @microsoft/aspire-cli dashboard run --allow-anonymous
```

Expected output:

```text
Dashboard:  http://localhost:18888
OTLP/gRPC:  http://localhost:4317
OTLP/HTTP:  http://localhost:4318
```

Then start the backend. The backend has this config:

```json
"OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317",
"OTEL_EXPORTER_OTLP_PROTOCOL": "grpc",
"OTEL_SERVICE_NAME": "TaskFlow.Api"
```

That means:

```text
TaskFlow backend -> OTLP/gRPC receiver on 4317 -> Aspire Dashboard UI on 18888
```

Generate a quick trace:

```powershell
Invoke-RestMethod -Uri "http://localhost:5198/api/analytics/metrics" -Method Get
```

Generate a custom agent trace:

```powershell
$body = @{
  featureRequest = "Add a compact visual telemetry indicator to the dashboard"
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "http://localhost:5198/api/agents/plan-feature" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

Then open:

```text
http://localhost:18888
```

Go to **Traces** and look for:

- `TaskFlow.Api: GET /api/analytics/metrics`
- `TaskFlow.Api: POST /api/agents/plan-feature`

The agent trace should show custom spans such as:

```text
POST /api/agents/plan-feature
  PlanFeaturePipeline
    PlannerAgent
    DeveloperReviewAttempt
      DeveloperAgent
      ReviewerAgent
```

You can also query traces from the terminal:

```powershell
npx -y @microsoft/aspire-cli otel traces --dashboard-url "http://localhost:18888"
```

## Ollama

Ollama is the local model server used by the backend for:

- Chat generation: `llama3.2`
- Embeddings: `nomic-embed-text`

Check Ollama:

```powershell
Invoke-WebRequest -Uri "http://localhost:11434" -UseBasicParsing
```

List models:

```powershell
ollama list
```

If missing, pull them:

```powershell
ollama pull llama3.2
ollama pull nomic-embed-text
```

## Optional Qdrant

Qdrant is only needed for the dedicated Qdrant comparison endpoints:

- `POST /api/rag/qdrant/ingest`
- `POST /api/rag/qdrant/ask`

If Qdrant is not running, the SQL hybrid RAG and Kernel Memory flows can still work.

Expected Qdrant ports:

```text
REST: http://localhost:6333
GRPC: localhost:6334
```

Check Qdrant REST:

```powershell
Invoke-RestMethod http://localhost:6333/
```

## Optional MCP Server

MCP servers are separate tool processes, not normal web servers with browser URLs. Claude Desktop starts them from `%APPDATA%\Claude\claude_desktop_config.json` when the app launches.

Configured Claude Desktop MCP servers:

| Server | Purpose | Launch Command |
|---|---|---|
| `taskflow-workitems` | Custom TaskFlow database/work-item and telemetry tools | `dotnet run --project mcp-server/mcp-server.csproj --no-build` |
| `playwright` | Existing Microsoft Playwright browser automation MCP | `npx @playwright/mcp@latest` |
| `filesystem` | Existing MCP for reading/editing files inside this workspace only | `cmd /c npx -y @modelcontextprotocol/server-filesystem <workspace>` |
| `github` | Official GitHub MCP for read-only repo, issue, and pull request context | `github-mcp-server.exe stdio --read-only` |
| `tavily` | Tavily remote MCP for current web search, extraction, crawl, and site mapping | `cmd /c npx -y mcp-remote https://mcp.tavily.com/mcp` |

After changing Claude's MCP config, fully quit and reopen Claude Desktop. Opening a second Claude window is not enough if the old process is still running.

Stop Claude completely if needed:

```powershell
Get-Process claude -ErrorAction SilentlyContinue
Stop-Process -Name claude -Force
```

### Custom TaskFlow MCP

The TaskFlow MCP server exposes controlled business tools for the local TaskFlow database and read-only telemetry tools for the local Aspire Dashboard.

VS Code workspace config is in:

```text
.vscode/mcp.json
```

It starts:

```powershell
dotnet run --project mcp-server/mcp-server.csproj
```

Use it only from an MCP-capable client such as Claude Desktop, MCP inspector, or VS Code MCP support if enabled in the environment.

Important: MCP stdio transport uses stdout for JSON-RPC. Logging must go to stderr, which is already configured in `mcp-server/Program.cs`.

Verify TaskFlow MCP tools with the inspector:

```powershell
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
npx -y @modelcontextprotocol/inspector --cli --config $configPath --server taskflow-workitems --method tools/list
```

Expected tools include:

```text
get_workload_summary
get_work_items_by_status
get_overdue_high_priority_items
create_work_item
update_work_item_status
update_work_item_priority
get_recent_telemetry_traces
get_failed_telemetry_traces
get_slowest_telemetry_traces
get_telemetry_spans_for_trace
```

TaskFlow telemetry MCP tools query the local Aspire Dashboard directly at `http://localhost:18888/api/telemetry/traces`. They do not mutate app state and do not require a production telemetry backend.

Example telemetry prompts for Claude Desktop:

```text
Use taskflow-workitems to get the slowest recent telemetry traces and explain which spans took the longest.
```

```text
Use taskflow-workitems to get recent failed telemetry traces. If any exist, summarize the likely cause and include the Aspire dashboard link.
```

```text
Use taskflow-workitems to get recent traces matching /api/rag, then inspect spans for the slowest trace.
```

### Playwright MCP

Playwright MCP lets Claude Desktop drive a real browser against the Angular app. It is useful for UI smoke tests, form interactions, navigation checks, and network inspection.

Verify Playwright MCP tools:

```powershell
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
npx -y @modelcontextprotocol/inspector --cli --config $configPath --server playwright --method tools/list
```

Useful Playwright MCP tools include:

```text
browser_navigate
browser_snapshot
browser_click
browser_type
browser_fill_form
browser_network_requests
browser_take_screenshot
browser_close
```

Example Claude prompt after restart:

```text
Use Playwright to open http://localhost:4200, inspect the TaskFlow UI, and verify the Analytics tab loads.
```

### Filesystem MCP

Filesystem MCP lets Claude Desktop read, search, and edit files, but it is scoped only to this workspace:

```text
C:\Users\bibekgurung\Desktop\Rapid\Extras\AI-Learn
```

Do not expand it to `C:\`, `%USERPROFILE%`, `%APPDATA%`, or other broad folders. Keep it scoped to the project so Claude can help with code/docs without seeing unrelated personal or system files.

Verify filesystem MCP tools:

```powershell
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
npx -y @modelcontextprotocol/inspector --cli --config $configPath --server filesystem --method tools/list
```

Verify the sandboxed directory:

```powershell
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
npx -y @modelcontextprotocol/inspector --cli --config $configPath --server filesystem --method tools/call --tool-name list_allowed_directories --tool-arg dummy=unused
```

Useful filesystem MCP tools include:

```text
read_text_file
read_multiple_files
list_directory
directory_tree
search_files
get_file_info
edit_file
write_file
list_allowed_directories
```

Recommended Claude prompt:

```text
Use filesystem to read PROJECT_STARTUP_GUIDE.md and summarize the startup order. Do not modify any files.
```

For edits, ask Claude to preview intent first:

```text
Use filesystem to inspect PROJECT_STARTUP_GUIDE.md and propose a documentation update. Show me the proposed change before editing.
```

### GitHub MCP

GitHub MCP lets Claude Desktop inspect GitHub repositories, issues, and pull requests. This setup uses the official GitHub MCP Server Windows binary, not the deprecated npm GitHub server and not Docker.

Installed binary:

```text
%LOCALAPPDATA%\GitHubMcpServer\github-mcp-server.exe
```

Current Claude config is intentionally conservative:

```text
read-only: true
toolsets: repos, issues, pull_requests
oauth scopes: public_repo, read:user, user:email
```

On first use, GitHub MCP may open a browser authorization flow. Complete that login in the browser, then retry the Claude request. The token is handled by the GitHub MCP server flow; do not paste GitHub tokens into chat.

If the target repository is private, `public_repo` may not be enough. In that case, use a GitHub Personal Access Token or a broader OAuth scope, but store it outside Git-tracked files and outside chat.

Verify GitHub MCP tools:

```powershell
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
npx -y @modelcontextprotocol/inspector --cli --config $configPath --server github --method tools/list
```

Example Claude prompts:

```text
Use github to inspect repository grgbibek/AI-Learn and summarize open issues or pull requests.
```

```text
Use github to search code in grgbibek/AI-Learn for OpenTelemetry setup and summarize the relevant files.
```

```text
Use github to review recent pull request context for grgbibek/AI-Learn. Do not create or modify anything.
```

### Tavily MCP

Tavily MCP gives Claude Desktop current web search and extraction tools. It is useful for checking fast-moving AI docs, package guidance, framework comparisons, and official documentation.

This setup uses Tavily's remote MCP through `mcp-remote`, so no Tavily API key is stored in `claude_desktop_config.json`:

```text
cmd /c npx -y mcp-remote https://mcp.tavily.com/mcp
```

On first use, the Tavily MCP may open a browser authorization flow. Complete the sign-in/authorization in the browser, then retry the Claude request.

Verify Tavily MCP tools:

```powershell
$configPath = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
npx -y @modelcontextprotocol/inspector --cli --config $configPath --server tavily --method tools/list
```

Expected tools include:

```text
tavily_search
tavily_extract
tavily_crawl
tavily_map
```

Example Claude prompts:

```text
Use tavily to search for current OpenTelemetry .NET OTLP exporter guidance. Prefer official docs and summarize what applies to TaskFlow.
```

```text
Use tavily to find current Semantic Kernel Agents .NET documentation and tell me whether our hand-rolled agent pipeline should be compared with it next.
```

```text
Use tavily to compare Azure AI Search, Pinecone, Qdrant, and SQL Server vector search for a .NET RAG app.
```

## Smoke Tests

Backend reachable:

```powershell
Invoke-WebRequest -Uri "http://localhost:5198/scalar" -UseBasicParsing
```

Frontend reachable:

```powershell
Invoke-WebRequest -Uri "http://localhost:4200" -UseBasicParsing
```

Aspire Dashboard reachable:

```powershell
Invoke-WebRequest -Uri "http://localhost:18888" -UseBasicParsing
```

Ollama reachable:

```powershell
Invoke-WebRequest -Uri "http://localhost:11434" -UseBasicParsing
```

Analytics endpoint:

```powershell
Invoke-RestMethod -Uri "http://localhost:5198/api/analytics/metrics" -Method Get
```

Semantic similarity endpoint:

```powershell
$body = @{
  text1 = "Angular signals manage frontend state"
  text2 = "Angular signal state management in UI components"
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "http://localhost:5198/api/ai/semantic-similarity" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

## Common Problems

### Backend build fails because `backend.exe` is locked

This means a backend process is already running. Find and stop it:

```powershell
Get-Process backend -ErrorAction SilentlyContinue
Stop-Process -Name backend -Force
```

Then rebuild:

```powershell
dotnet build backend/backend.csproj
```

### Aspire Dashboard UI opens but no traces appear

Check:

1. Aspire Dashboard is running.
2. Backend was started after the dashboard.
3. Backend config has `OTEL_EXPORTER_OTLP_ENDPOINT` set to `http://localhost:4317`.
4. You called at least one backend endpoint after startup.

Query from terminal:

```powershell
npx -y @microsoft/aspire-cli otel traces --dashboard-url "http://localhost:18888"
```

### `http://localhost:4317` does not show a web page

That is expected. It is the OTLP/gRPC telemetry receiver, not a browser UI.

Open the dashboard UI instead:

```text
http://localhost:18888
```

### Ollama requests fail

Check Ollama is running and models exist:

```powershell
ollama list
Invoke-WebRequest -Uri "http://localhost:11434" -UseBasicParsing
```

Pull missing models:

```powershell
ollama pull llama3.2
ollama pull nomic-embed-text
```

### Qdrant endpoints fail

Only Qdrant comparison endpoints require Qdrant. Start Qdrant or use SQL hybrid RAG / Kernel Memory instead.

### Frontend cannot call backend

Check backend is running on `5198`, and the frontend origin is allowed by CORS. Current backend CORS allows:

```text
http://localhost:4200
http://localhost:4201
```

## Recommended Daily Startup Order

1. Start Ollama.
2. Start Aspire Dashboard.
3. Start backend.
4. Start frontend.
5. Open `http://localhost:4200`.
6. Open `http://localhost:18888` when you want traces.
7. Start Qdrant only when testing Qdrant RAG.
8. Start MCP only when testing MCP clients.

## Useful Build Commands

```powershell
# Backend build
dotnet build backend/backend.csproj

# Backend run
dotnet run --project backend/backend.csproj

# Frontend install
Push-Location frontend
npm install
Pop-Location

# Frontend build
Push-Location frontend
npm run build
Pop-Location

# Frontend dev server
Push-Location frontend
npm start
Pop-Location
```
