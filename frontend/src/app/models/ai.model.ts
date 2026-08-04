export interface SubtaskAnalysisResponse {
  workItemId: number;
  originalTitle: string;
  subtasks: string[];
  estimatedTotalHours: number;
  complexityLevel: string;
}

export interface WorkloadAssistantResponse {
  prompt: string;
  toolRegistered: string;
  response: string;
}

export interface SemanticSimilarityResponse {
  text1: string;
  text2: string;
  cosineSimilarityScore: number;
  interpretation: string;
  vectorDimensions: number;
}
