## Goal

Add a `/api/health` endpoint that returns DB connectivity, overall status, and Ollama reachability. The endpoint should report 200 when healthy and 503 when not. Additionally, add a corresponding Angular health card on the dashboard.

## Scope

### Backend
- Add a new `/api/health` endpoint that checks DB connectivity and Ollama reachability.
- Return 200 when healthy, 503 when not.
- Update the API contract.

### Frontend
- Add a new Angular health card on the dashboard to display the health status.

### Data / Persistence
- No data migrations required for this feature.

### Security & Guardrails
- Ensure the endpoint is protected by appropriate authentication and authorization mechanisms.

## Task Breakdown

1. **Backend**
   - Design the `/api/health` endpoint.
   - Implement the logic to check DB connectivity and Ollama reachability.
   - Update the API contract to reflect the new endpoint.

2. **Frontend**
   - Design and implement the Angular health card component.
   - Integrate the health card with the existing dashboard.

3. **Security**
   - Ensure the endpoint is secured using appropriate authentication and authorization mechanisms.

## Validation Plan

1. **Backend**
   - Manually test the `/api/health` endpoint using Postman or curl.
   - Verify the response status codes for healthy and unhealthy states.

2. **Frontend**
   - Verify that the health card displays the correct health status.
   - Ensure the health card updates in real-time as the backend health status changes.

## Open Questions

- What are the specific authentication and authorization mechanisms required for the `/api/health` endpoint?
- Should the health card be displayed on the main dashboard or a separate page?

## Implementation Approval

(Leave this section empty until approved by a human or trusted orchestrator)