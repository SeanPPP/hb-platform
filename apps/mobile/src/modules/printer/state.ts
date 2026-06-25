import { create } from "zustand";
import type { SavedPrinter } from "@/modules/printer/types";

export type PrinterConnectionState =
  | "idle"
  | "connecting"
  | "connected"
  | "reconnecting"
  | "disconnected"
  | "paused"
  | "error";

interface PrinterState {
  savedPrinter: SavedPrinter | null;
  status: PrinterConnectionState;
  autoReconnectPaused: boolean;
  lastError: string | null;
  hydrated: boolean;
  setSavedPrinter: (printer: SavedPrinter | null) => void;
  setStatus: (status: PrinterConnectionState) => void;
  setAutoReconnectPaused: (paused: boolean) => void;
  setLastError: (message: string | null) => void;
  setHydrated: (hydrated: boolean) => void;
}

export const usePrinterStore = create<PrinterState>((set) => ({
  savedPrinter: null,
  status: "idle",
  autoReconnectPaused: false,
  lastError: null,
  hydrated: false,
  setSavedPrinter: (savedPrinter) => set({ savedPrinter }),
  setStatus: (status) => set({ status }),
  setAutoReconnectPaused: (autoReconnectPaused) => set({ autoReconnectPaused }),
  setLastError: (lastError) => set({ lastError }),
  setHydrated: (hydrated) => set({ hydrated }),
}));

// 小票打印机不参与标签打印自动重连，单独维护状态，避免影响商品查询和标签打印链路。
export const useReceiptPrinterStore = create<PrinterState>((set) => ({
  savedPrinter: null,
  status: "idle",
  autoReconnectPaused: false,
  lastError: null,
  hydrated: false,
  setSavedPrinter: (savedPrinter) => set({ savedPrinter }),
  setStatus: (status) => set({ status }),
  setAutoReconnectPaused: (autoReconnectPaused) => set({ autoReconnectPaused }),
  setLastError: (lastError) => set({ lastError }),
  setHydrated: (hydrated) => set({ hydrated }),
}));
