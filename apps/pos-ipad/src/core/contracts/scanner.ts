export type ScannerContext =
  | "cashier-login"
  | "product"
  | "product-search"
  | "supervisor-authorization"
  | "dialog";

export type ScanSource = "hid" | "camera";

export type CameraScanMode = "single" | "continuous";

export type ScanEvent = Readonly<{
  value: string;
  source: ScanSource;
  context: ScannerContext;
  receivedAtIso: string;
}>;

export interface ScannerPort {
  setContext(context: ScannerContext): void;
  startCamera(): Promise<void>;
  stopCamera(): Promise<void>;
  subscribe(listener: (event: ScanEvent) => void): () => void;
}
