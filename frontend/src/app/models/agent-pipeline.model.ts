export interface AgentPipelineResult {
  subtask: string;
  technicalApproach: string;
  approved: boolean;
  feedback: string;
}

export interface PlanFeatureResponse {
  featureRequest: string;
  results: AgentPipelineResult[];
}

export interface AgentAuditLogEntry {
  id: number;
  featureRequest: string;
  subtask: string;
  attemptNumber: number;
  technicalApproach: string;
  approved: boolean;
  feedback: string;
  createdAt: string;
}
