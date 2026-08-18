```markdown
## Behavior Under Test
The `/api/health` endpoint checks DB connectivity, overall status, and Ollama reachability, returning 200 when healthy and 503 when not.

## Risk Map
1. **Security Risk**: The `/api/health` endpoint is exposed without proper authentication and authorization mechanisms.
2. **Backend Check**: The logic for checking Ollama reachability is not implemented.
3. **Frontend Check**: The health card component does not handle the case when the backend is unreachable.
4. **Test Gap**: No tests for the `/api/health` endpoint and the health card component.

## Backend Tests
1. **Unit Test**: Verify the logic for checking DB connectivity.
   ```bash
   dotnet test backend/tests/HealthControllerTests.cs --filter "HealthControllerTests.DatabaseConnectivityCheck"
   ```

2. **Integration Test**: Verify the logic for checking Ollama reachability.
   ```bash
   dotnet test backend/tests/HealthControllerTests.cs --filter "HealthControllerTests.OllamaReachabilityCheck"
   ```

3. **Security Test**: Ensure the endpoint requires authentication and authorization.
   ```bash
   curl -X GET /api/health -H "Authorization: Bearer $(curl /api/auth/dev-token | jq -r .token)"
   ```

## Frontend Tests
1. **Unit Test**: Verify the health card component displays the correct health status.
   ```bash
   ng test frontend --include=HealthCardComponent
   ```

2. **Integration Test**: Verify the health card updates in real-time.
   - Simulate backend health changes and verify the health card updates accordingly.

## Integration / Smoke Tests
1. **Backend Smoke Test**: Verify the `/api/health` endpoint returns appropriate status codes.
   ```bash
   curl -X GET /api/health -H "Authorization: Bearer $(curl /api/auth/dev-token | jq -r .token)" -I
   ```

2. **Frontend Smoke Test**: Verify the health card displays the correct health status on the dashboard.
   - Manually verify the health card status on the dashboard.

## Suggested Commands
```powershell
dotnet build backend/backend.csproj
npm --prefix frontend run build
dotnet test backend/tests/HealthControllerTests.cs
ng test frontend --include=HealthCardComponent
```

## What Not To Test Yet
- Tests for state transitions and cancellation are not applicable for this feature.
```