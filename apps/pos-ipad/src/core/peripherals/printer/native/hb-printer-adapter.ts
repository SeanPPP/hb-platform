import type { CashDrawerPort, DrawerResult } from "../../../contracts/drawer";
import type {
  PrintResult,
  PrinterDevice,
  PrinterPort,
  PrinterStatus,
} from "../../../contracts/printer";

import { createHbPrinterBridge, type NativeModuleLoader } from "./bridge";
import type {
  BluetoothPrinterStatus,
  HbPrinterBridge,
  PrinterEncoding,
  PrinterNativeEvent,
  PrinterOperationResult,
} from "./types";

const MIN_TIMEOUT_MS = 3_000;
const MAX_TIMEOUT_MS = 120_000;

export class PrinterOutcomeUnknownError extends Error {
  readonly code = "PRINTER_OUTCOME_UNKNOWN";

  constructor(readonly result: PrinterOperationResult) {
    super(result.message ?? "打印或钱箱操作结果未知，禁止自动重放。");
    this.name = "PrinterOutcomeUnknownError";
  }
}

export type ScanPrintersOptions = {
  durationMs?: number;
  includeNonXprinter?: boolean;
};

export type PrintTextRequest = {
  operationId: string;
  text: string;
  encoding?: PrinterEncoding;
  appendLineFeed?: boolean;
  cutAfterPrint?: boolean;
  timeoutMs?: number;
};

export type DrawerPulseRequest = {
  operationId: string;
  pin?: 0 | 1;
  onTime?: number;
  offTime?: number;
  timeoutMs?: number;
};

/**
 * POS 领域层的原生打印适配器。unknown 结果抛出专用错误，调用方必须入库后由人工决定是否重试。
 */
export class HbPrinterNativeAdapter implements PrinterPort, CashDrawerPort {
  private transientStatus: PrinterStatus | null = null;

  constructor(private readonly bridge: HbPrinterBridge) {}

  async getStatus(): Promise<PrinterStatus> {
    if (this.transientStatus === "scanning") {
      return this.transientStatus;
    }
    return toPrinterStatus(await this.bridge.getStatus());
  }

  async scan(timeoutOrOptions: number | ScanPrintersOptions = {}): Promise<readonly PrinterDevice[]> {
    const options = typeof timeoutOrOptions === "number" ? { durationMs: timeoutOrOptions } : timeoutOrOptions;
    this.transientStatus = "scanning";
    try {
      return await this.bridge.scan(
        boundedTimeout(options.durationMs ?? 5_000),
        options.includeNonXprinter ?? false,
      );
    } finally {
      this.transientStatus = null;
    }
  }

  async connect(peripheralId: string, timeoutMs?: number): Promise<void> {
    await this.bridge.connect(
      requiredText(peripheralId, "peripheralId"),
      boundedTimeout(timeoutMs ?? 12_000),
    );
  }

  async disconnect(): Promise<void> {
    await this.bridge.disconnect();
  }

  async print(jobId: string, bytes: Uint8Array): Promise<PrintResult> {
    try {
      return toPrintResult(
        await this.bridge.write(
          requiredText(jobId, "jobId"),
          [...bytes],
          30_000,
          "print",
        ),
      );
    } catch (error) {
      return { status: "failed", errorCode: errorCodeOf(error) };
    }
  }

  async open(eventId: string): Promise<DrawerResult> {
    try {
      return toDrawerResult(
        await this.bridge.openCashDrawer(
          requiredText(eventId, "eventId"),
          0,
          25,
          255,
          10_000,
        ),
      );
    } catch (error) {
      return { status: "failed", errorCode: errorCodeOf(error) };
    }
  }

  async printText(request: PrintTextRequest): Promise<PrinterOperationResult> {
    const result = await this.bridge.printText(
      requiredText(request.operationId, "operationId"),
      request.text,
      request.encoding ?? "gb18030",
      request.appendLineFeed ?? true,
      request.cutAfterPrint ?? false,
      boundedTimeout(request.timeoutMs ?? 30_000),
    );
    return assertKnownOutcome(result);
  }

  async printRaw(
    operationId: string,
    bytes: Uint8Array,
    timeoutMs?: number,
  ): Promise<PrinterOperationResult> {
    const result = await this.bridge.write(
      requiredText(operationId, "operationId"),
      [...bytes],
      boundedTimeout(timeoutMs ?? 30_000),
      "print",
    );
    return assertKnownOutcome(result);
  }

  async openCashDrawer(request: DrawerPulseRequest): Promise<PrinterOperationResult> {
    const result = await this.bridge.openCashDrawer(
      requiredText(request.operationId, "operationId"),
      request.pin ?? 0,
      boundedPulse(request.onTime ?? 25),
      boundedPulse(request.offTime ?? 255),
      boundedTimeout(request.timeoutMs ?? 10_000),
    );
    return assertKnownOutcome(result);
  }

  subscribe(listener: (status: PrinterStatus) => void): () => void {
    return this.bridge.subscribe((event) => {
      if (event.type === "status") {
        this.transientStatus = null;
        listener(toPrinterStatus(event));
      } else if (event.type === "error") {
        listener("failed");
      }
    });
  }

  subscribeNative(listener: (event: PrinterNativeEvent) => void): () => void {
    return this.bridge.subscribe((event) => {
      if (event.type === "status") {
        this.transientStatus = null;
      }
      listener(event);
    });
  }
}

export function createDefaultHbPrinterAdapter(loader: NativeModuleLoader): HbPrinterNativeAdapter {
  return new HbPrinterNativeAdapter(createHbPrinterBridge(loader));
}

function assertKnownOutcome(result: PrinterOperationResult): PrinterOperationResult {
  if (result.state === "unknown") {
    throw new PrinterOutcomeUnknownError(result);
  }
  return result;
}

function requiredText(value: string, fieldName: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new Error(`${fieldName} 不能为空。`);
  }
  return normalized;
}

function boundedTimeout(value: number): number {
  if (!Number.isInteger(value) || value < MIN_TIMEOUT_MS || value > MAX_TIMEOUT_MS) {
    throw new Error(`timeoutMs 必须是 ${MIN_TIMEOUT_MS} 到 ${MAX_TIMEOUT_MS} 之间的整数。`);
  }
  return value;
}

function boundedPulse(value: number): number {
  if (!Number.isInteger(value) || value < 1 || value > 255) {
    throw new Error("钱箱脉冲必须是 1 到 255 之间的整数。");
  }
  return value;
}

function toPrinterStatus(status: BluetoothPrinterStatus): PrinterStatus {
  if (!status.supported || !status.enabled) {
    return "unavailable";
  }
  return status.connection;
}

function toPrintResult(result: PrinterOperationResult): PrintResult {
  switch (result.state) {
    case "completed":
      return { status: "printed", errorCode: null };
    case "failed":
      return { status: "failed", errorCode: "PRINTER_OPERATION_FAILED" };
    case "unknown":
      // 合同中的 ambiguous 会阻止 spool 自动重放，避免重复打印或重复开钱箱。
      return { status: "ambiguous", errorCode: "PRINTER_OUTCOME_UNKNOWN" };
  }
}

function toDrawerResult(result: PrinterOperationResult): DrawerResult {
  switch (result.state) {
    case "completed":
      return { status: "completed", errorCode: null };
    case "failed":
      return { status: "failed", errorCode: "PRINTER_OPERATION_FAILED" };
    case "unknown":
      return { status: "unknown", errorCode: "PRINTER_OUTCOME_UNKNOWN" };
  }
}

function errorCodeOf(error: unknown): string {
  if (typeof error === "object" && error !== null && "code" in error && typeof error.code === "string") {
    return error.code;
  }
  return "PRINTER_NATIVE_ERROR";
}
