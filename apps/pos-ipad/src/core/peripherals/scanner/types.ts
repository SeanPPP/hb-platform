import type { ScanEvent, ScannerContext } from "../../contracts/scanner";

export type ScannerCaptureContext =
  | ScannerContext
  | "emergency-qr"
  | "device-activation";

export type ScannerScanCategory =
  | "cashier-code"
  | "product-code"
  | "supervisor-code"
  | "dialog-code"
  | "emergency-qr"
  | "device-activation";

export type ScannerCaptureStatus = "inactive" | "capturing" | "camera";

export type RoutedScanEvent = Readonly<
  Omit<ScanEvent, "context"> & {
    context: ScannerCaptureContext;
    category: ScannerScanCategory;
  }
>;

export type TextInputCaptureResult = Readonly<{
  valueToRender: string;
  emitted: boolean;
  overflowed: boolean;
}>;
