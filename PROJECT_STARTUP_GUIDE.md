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

The MCP server is a separate stdio process, not a web server with a browser URL.

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
