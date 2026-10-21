import { create } from 'zustand';
import { aiApi } from '../api/endpoints';
import { toApiError } from '../api/errors';
import type { ApiError, AskResponse, MetricsSummary } from '../types';

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  response?: AskResponse;
  error?: ApiError;
}

interface AiState {
  messages: ChatMessage[];
  asking: boolean;
  summary: MetricsSummary | null;
  summaryError: ApiError | null;
  ask: (question: string, useCache: boolean) => Promise<void>;
  loadSummary: () => Promise<void>;
  reset: () => void;
}

let seq = 0;
const id = () => `m${++seq}`;

export const useAiStore = create<AiState>()((set, get) => ({
  messages: [],
  asking: false,
  summary: null,
  summaryError: null,

  ask: async (question, useCache) => {
    const q = question.trim();
    set({ asking: true, messages: [...get().messages, { id: id(), role: 'user', content: q }] });
    try {
      const response = await aiApi.ask(q, useCache);
      set({ messages: [...get().messages, { id: id(), role: 'assistant', content: response.answer, response }] });
    } catch (e) {
      const error = toApiError(e);
      set({ messages: [...get().messages, { id: id(), role: 'assistant', content: error.message, error }] });
    } finally {
      set({ asking: false });
    }
  },

  loadSummary: async () => {
    try {
      set({ summary: await aiApi.metrics(), summaryError: null });
    } catch (e) {
      set({ summaryError: toApiError(e) });
    }
  },

  reset: () => set({ messages: [] })
}));
