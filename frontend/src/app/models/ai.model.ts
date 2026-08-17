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

export interface StreamingAssistantResponse {
  prompt: string;
  answer: string;
  wasSanitized?: boolean;
  detectedTypes?: string[];
}

export interface AiConversationSummaryResponse {
  id: number;
  title: string;
  createdAt: string;
  updatedAt: string;
  lastMessagePreview?: string;
  messageCount: number;
}

export type AiConversationRole = 'user' | 'assistant';

export interface AiConversationMessageResponse {
  id: number;
  role: AiConversationRole;
  content: string;
  createdAt: string;
  wasSanitized: boolean;
  detectedTypes: readonly string[];
}

export interface AiConversationResponse {
  id: number;
  title: string;
  createdAt: string;
  updatedAt: string;
  messages: AiConversationMessageResponse[];
}

export type StreamingAssistantStatus = 'idle' | 'loadingConversations' | 'loadingConversation' | 'streaming' | 'error';

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

