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

export interface IngestDocumentResponse {
  title: string;
  chunksCreated?: number;
  documentId?: string;
  store?: string;
}

export interface KernelMemoryPartition {
  sanitizedText: string;
  relevance: number;
}

export interface KnowledgeSource {
  sourceTitle?: string;
  sourceName?: string;
  chunkIndex?: number;
  vectorScore?: number;
  keywordScore?: number;
  fusedScore?: number;
  rerankPosition?: number;
  partitions?: KernelMemoryPartition[];
}

export interface AskKnowledgeBaseResponse {
  question: string;
  answer: string;
  rerankMethod?: string;
  store?: string;
  sources: KnowledgeSource[];
}

