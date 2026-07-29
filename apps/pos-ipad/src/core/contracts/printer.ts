export type PrinterStatus = "unavailable" | "disconnected" | "scanning" | "connecting" | "ready" | "failed";

export type PrinterDevice = Readonly<{
  id: string;
  name: string;
  rssi: number | null;
}>;

export type PrintResult = Readonly<{
  status: "printed" | "failed" | "ambiguous";
  errorCode: string | null;
}>;

export interface PrinterPort {
  getStatus(): Promise<PrinterStatus>;
  scan(timeoutMs: number): Promise<readonly PrinterDevice[]>;
  connect(deviceId: string): Promise<void>;
  disconnect(): Promise<void>;
  print(jobId: string, bytes: Uint8Array): Promise<PrintResult>;
  subscribe(listener: (status: PrinterStatus) => void): () => void;
}
