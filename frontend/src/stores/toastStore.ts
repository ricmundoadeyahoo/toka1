import { create } from 'zustand';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

interface ToastState {
  toasts: Toast[];
  push: (kind: ToastKind, message: string, ttlMs?: number) => void;
  dismiss: (id: number) => void;
}

let nextId = 1;

/** Feedback visual global (notificaciones efímeras). */
export const useToastStore = create<ToastState>()((set, get) => ({
  toasts: [],
  push: (kind, message, ttlMs = 4000) => {
    const id = nextId++;
    set({ toasts: [...get().toasts, { id, kind, message }] });
    if (ttlMs > 0) setTimeout(() => get().dismiss(id), ttlMs);
  },
  dismiss: (id) => set({ toasts: get().toasts.filter((t) => t.id !== id) })
}));

export const toast = {
  success: (message: string) => useToastStore.getState().push('success', message),
  error: (message: string) => useToastStore.getState().push('error', message, 6000),
  info: (message: string) => useToastStore.getState().push('info', message)
};
