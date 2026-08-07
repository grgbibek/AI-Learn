import { Injectable, signal } from '@angular/core';
import { pipeline, TextClassificationPipeline } from '@huggingface/transformers';

export interface ToneResult {
  label: string;
  score: number;
}

// Runs a small sentiment-analysis model entirely in the browser (WebAssembly/WebGPU via
// Transformers.js) - no backend/network round trip once the model is cached, unlike the
// Ollama-backed AiService which always calls our .NET API.
@Injectable({
  providedIn: 'root'
})
export class ClientAiService {
  readonly modelLoading = signal<boolean>(false);
  readonly modelReady = signal<boolean>(false);

  private classifierPromise: Promise<TextClassificationPipeline> | null = null;

  private getClassifier(): Promise<TextClassificationPipeline> {
    if (!this.classifierPromise) {
      this.modelLoading.set(true);
      this.classifierPromise = pipeline(
        'sentiment-analysis',
        'Xenova/distilbert-base-uncased-finetuned-sst-2-english'
      ).then((classifier) => {
        this.modelLoading.set(false);
        this.modelReady.set(true);
        return classifier;
      }).catch((err) => {
        // Don't cache a failed load - clear it so the next call can retry (e.g. after a network blip).
        this.classifierPromise = null;
        this.modelLoading.set(false);
        throw err;
      });
    }
    return this.classifierPromise;
  }

  async classifyTone(text: string): Promise<ToneResult | null> {
    const classifier = await this.getClassifier();
    const output = await classifier(text);
    const result = Array.isArray(output) ? output[0] : output;
    return result ? { label: result.label, score: result.score } : null;
  }
}
