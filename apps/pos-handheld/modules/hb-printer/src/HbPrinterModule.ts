import { requireNativeModule } from "expo";

import type {
  PrinterDevice,
  PrinterEncoding,
  PrinterOperationResult,
  PrinterStatus,
} from "./HbPrinter.types";

type HbPrinterNativeModule = {
  getStatus(): Promise<PrinterStatus>;
  scan(durationMs: number, includeAll: boolean): Promise<PrinterDevice[]>;
  connect(peripheralId: string, timeoutMs: number): Promise<PrinterStatus>;
  disconnect(): Promise<PrinterStatus>;
  write(
    operationId: string,
    bytes: number[],
    timeoutMs: number,
    kind: "print" | "drawer",
  ): Promise<PrinterOperationResult>;
  printText(
    operationId: string,
    text: string,
    encoding: PrinterEncoding,
    appendLineFeed: boolean,
    cutAfterPrint: boolean,
    timeoutMs: number,
  ): Promise<PrinterOperationResult>;
  openCashDrawer(
    operationId: string,
    pin: number,
    onTime: number,
    offTime: number,
    timeoutMs: number,
  ): Promise<PrinterOperationResult>;
  addListener(
    eventName: string,
    listener: (event: unknown) => void,
  ): { remove(): void };
};

export default requireNativeModule<HbPrinterNativeModule>("HbPrinter");
