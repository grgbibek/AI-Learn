import { Injectable, signal } from '@angular/core';
import { pipeline, TextClassificationPipeline } from '@huggingface/transformers';

export interface ToneResult {
  label: string;
  score: number;
  engine: 'transformers' | 'local-fallback';
}

@Injectable({
  providedIn: 'root'
})
export class ClientAiService {
  readonly modelLoading = signal<boolean>(false);
  readonly modelReady = signal<boolean>(false);
  readonly activeEngine = signal<'transformers' | 'local-fallback'>('local-fallback');
  readonly lastAnalysis = signal<ToneResult | null>(null);

  private classifierPromise: Promise<TextClassificationPipeline | null> | null = null;
  private useFallback = false;

  private async getClassifierWithTimeout(): Promise<TextClassificationPipeline | null> {
    if (this.useFallback) return null;

    if (!this.classifierPromise) {
      this.modelLoading.set(true);

      const downloadPromise = pipeline(
        'sentiment-analysis',
        'Xenova/distilbert-base-uncased-finetuned-sst-2-english'
      );

      const timeoutPromise = new Promise<null>((_, reject) =>
        setTimeout(() => reject(new Error('HuggingFace model download timed out (network restriction)')), 3000)
      );

      this.classifierPromise = Promise.race([downloadPromise, timeoutPromise])
        .then((classifier) => {
          this.modelLoading.set(false);
          this.modelReady.set(true);
          this.activeEngine.set('transformers');
          return classifier as TextClassificationPipeline;
        })
        .catch((err) => {
          console.warn('Transformers.js failed or network restricted to huggingface.co. Falling back to local zero-latency NLP lexicon engine.', err);
          this.useFallback = true;
          this.modelLoading.set(false);
          this.modelReady.set(true);
          this.activeEngine.set('local-fallback');
          this.classifierPromise = null;
          return null;
        });
    }

    return this.classifierPromise;
  }

  async classifyTone(text: string): Promise<ToneResult> {
    if (!text || !text.trim()) {
      const result: ToneResult = { label: 'NEUTRAL', score: 0.5, engine: this.activeEngine() };
      this.lastAnalysis.set(result);
      return result;
    }

    try {
      const classifier = await this.getClassifierWithTimeout();
      if (classifier) {
        const output = await classifier(text);
        const res = Array.isArray(output) ? output[0] : output;
        if (res) {
          const toneRes: ToneResult = {
            label: res.label.toUpperCase(),
            score: Math.round(res.score * 100) / 100,
            engine: 'transformers'
          };
          this.lastAnalysis.set(toneRes);
          return toneRes;
        }
      }
    } catch (e) {
      console.warn('Transformers.js runtime error, using local fallback NLP classifier', e);
    }

    // Local Fallback Lexicon sentiment & urgency analysis engine
    const fallbackResult = this.classifyLocalLexicon(text);
    this.lastAnalysis.set(fallbackResult);
    return fallbackResult;
  }

  private classifyLocalLexicon(text: string): ToneResult {
    const lower = text.toLowerCase();

    const positiveWords = ['great', 'awesome', 'excellent', 'good', 'success', 'done', 'fixed', 'improve', 'feature', 'easy', 'complete', 'love', 'boost', 'fast'];
    const negativeUrgentWords = ['bug', 'error', 'fail', 'urgent', 'critical', 'broken', 'issue', 'crash', 'fatal', 'leak', 'slow', 'blocker', 'severe', 'help', 'risk'];

    let posScore = 0;
    let negScore = 0;

    positiveWords.forEach(word => {
      if (lower.includes(word)) posScore += 1;
    });

    negativeUrgentWords.forEach(word => {
      if (lower.includes(word)) negScore += 1.5;
    });

    let label = 'NEUTRAL';
    let confidence = 0.65;

    if (negScore > posScore) {
      label = 'NEGATIVE';
      confidence = Math.min(0.98, 0.70 + negScore * 0.1);
    } else if (posScore > negScore) {
      label = 'POSITIVE';
      confidence = Math.min(0.98, 0.70 + posScore * 0.1);
    } else {
      label = 'NEUTRAL';
      confidence = 0.50;
    }

    return {
      label,
      score: Math.round(confidence * 100) / 100,
      engine: 'local-fallback'
    };
  }
}
