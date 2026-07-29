import { create } from "zustand";

import type { DisplayStatus, PrinterStatus } from "@/core/contracts";
import type { ScannerCaptureStatus } from "@/core/peripherals/scanner";

export type ConnectivityStatus = "checking" | "online" | "offline";
export type DeviceGateStatus =
  | "unregistered"
  | "pending-approval"
  | "authorized"
  | "locked";

export type TerminalPresentation = Readonly<{
  storeName: string | null;
  deviceCode: string;
}>;

type PosShellState = {
  connectivity: ConnectivityStatus;
  deviceGate: DeviceGateStatus;
  pendingSyncCount: number;
  printer: PrinterStatus;
  scanner: ScannerCaptureStatus;
  display: DisplayStatus;
  terminalPresentation: TerminalPresentation | null;
  setConnectivity(status: ConnectivityStatus): void;
  setDeviceGate(status: DeviceGateStatus): void;
  setPendingSyncCount(count: number): void;
  setPrinter(status: PrinterStatus): void;
  setScanner(status: ScannerCaptureStatus): void;
  setDisplay(status: DisplayStatus): void;
  setTerminalPresentation(
    terminalPresentation: TerminalPresentation | null,
  ): void;
  reset(): void;
};

const initialStatus = {
  connectivity: "checking",
  deviceGate: "unregistered",
  pendingSyncCount: 0,
  printer: "disconnected",
  scanner: "inactive",
  display: "disconnected",
  terminalPresentation: null,
} as const;

export const usePosShellStore = create<PosShellState>((set) => ({
  ...initialStatus,
  setConnectivity: (connectivity) => set({ connectivity }),
  setDeviceGate: (deviceGate) => set({ deviceGate }),
  setPendingSyncCount: (pendingSyncCount) => {
    if (!Number.isSafeInteger(pendingSyncCount) || pendingSyncCount < 0) {
      throw new TypeError("pending sync count must be a non-negative safe integer");
    }
    set({ pendingSyncCount });
  },
  setPrinter: (printer) => set({ printer }),
  setScanner: (scanner) => set({ scanner }),
  setDisplay: (display) => set({ display }),
  setTerminalPresentation: (terminalPresentation) =>
    set({ terminalPresentation }),
  reset: () => set(initialStatus),
}));
