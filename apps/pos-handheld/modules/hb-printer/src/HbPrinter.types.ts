export type PrinterConnectionState =
  | "disconnected"
  | "connecting"
  | "ready"
  | "failed";

export type PrinterOperationState = "completed" | "failed" | "unknown";

export type PrinterDevice = {
  id: string;
  name: string;
  rssi: number | null;
  isXprinter: boolean;
};

export type PrinterStatus = {
  supported: boolean;
  enabled: boolean;
  connection: PrinterConnectionState;
  peripheralId: string | null;
  writeMode: "withResponse" | "withoutResponse" | null;
};

export type PrinterOperationResult = {
  operationId: string;
  state: PrinterOperationState;
  message: string | null;
};

export type PrinterStatusEvent = PrinterStatus & {
  reason: string;
};

export type PrinterOperationEvent = PrinterOperationResult & {
  kind: "print" | "drawer";
};

export type PrinterErrorEvent = {
  code: string;
  message: string;
};

export type PrinterEncoding = "gb18030" | "gbk" | "utf8";
