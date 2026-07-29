import type {
  BluetoothPrinterDevice,
  BluetoothPrinterStatus,
  HbPrinterBridge,
  PrinterEncoding,
  PrinterNativeEvent,
  PrinterOperationKind,
  PrinterOperationResult,
} from "./types";

type NativeSubscription = { remove(): void };

type NativeHbPrinterModule = Omit<HbPrinterBridge, "subscribe"> & {
  addListener(eventName: string, listener: (event: unknown) => void): NativeSubscription;
};

export class PrinterNativeUnavailableError extends Error {
  readonly code = "PRINTER_NATIVE_UNAVAILABLE";

  constructor() {
    super("HB POS 打印原生模块不可用；请安装包含 hb-printer 的 iPad Development Build。");
    this.name = "PrinterNativeUnavailableError";
  }
}

export type NativeModuleLoader = () => NativeHbPrinterModule;

const unavailableStatus: BluetoothPrinterStatus = {
  supported: false,
  enabled: false,
  connection: "failed",
  peripheralId: null,
  writeMode: null,
};

class UnavailableHbPrinterBridge implements HbPrinterBridge {
  async getStatus(): Promise<BluetoothPrinterStatus> {
    return unavailableStatus;
  }

  async scan(_durationMs: number, _includeAll: boolean): Promise<BluetoothPrinterDevice[]> {
    throw new PrinterNativeUnavailableError();
  }

  async connect(_peripheralId: string, _timeoutMs: number): Promise<BluetoothPrinterStatus> {
    throw new PrinterNativeUnavailableError();
  }

  async disconnect(): Promise<BluetoothPrinterStatus> {
    throw new PrinterNativeUnavailableError();
  }

  async write(
    _operationId: string,
    _bytes: number[],
    _timeoutMs: number,
    _kind: PrinterOperationKind,
  ): Promise<PrinterOperationResult> {
    throw new PrinterNativeUnavailableError();
  }

  async printText(
    _operationId: string,
    _text: string,
    _encoding: PrinterEncoding,
    _appendLineFeed: boolean,
    _cutAfterPrint: boolean,
    _timeoutMs: number,
  ): Promise<PrinterOperationResult> {
    throw new PrinterNativeUnavailableError();
  }

  async openCashDrawer(
    _operationId: string,
    _pin: 0 | 1,
    _onTime: number,
    _offTime: number,
    _timeoutMs: number,
  ): Promise<PrinterOperationResult> {
    throw new PrinterNativeUnavailableError();
  }

  subscribe(_listener: (event: PrinterNativeEvent) => void): () => void {
    return () => undefined;
  }
}

class ExpoHbPrinterBridge implements HbPrinterBridge {
  constructor(private readonly module: NativeHbPrinterModule) {}

  getStatus(): Promise<BluetoothPrinterStatus> {
    return this.module.getStatus();
  }

  scan(durationMs: number, includeAll: boolean): Promise<BluetoothPrinterDevice[]> {
    return this.module.scan(durationMs, includeAll);
  }

  connect(peripheralId: string, timeoutMs: number): Promise<BluetoothPrinterStatus> {
    return this.module.connect(peripheralId, timeoutMs);
  }

  disconnect(): Promise<BluetoothPrinterStatus> {
    return this.module.disconnect();
  }

  write(
    operationId: string,
    bytes: number[],
    timeoutMs: number,
    kind: PrinterOperationKind,
  ): Promise<PrinterOperationResult> {
    return this.module.write(operationId, bytes, timeoutMs, kind);
  }

  printText(
    operationId: string,
    text: string,
    encoding: PrinterEncoding,
    appendLineFeed: boolean,
    cutAfterPrint: boolean,
    timeoutMs: number,
  ): Promise<PrinterOperationResult> {
    return this.module.printText(
      operationId,
      text,
      encoding,
      appendLineFeed,
      cutAfterPrint,
      timeoutMs,
    );
  }

  openCashDrawer(
    operationId: string,
    pin: 0 | 1,
    onTime: number,
    offTime: number,
    timeoutMs: number,
  ): Promise<PrinterOperationResult> {
    return this.module.openCashDrawer(operationId, pin, onTime, offTime, timeoutMs);
  }

  subscribe(listener: (event: PrinterNativeEvent) => void): () => void {
    const subscriptions = [
      this.module.addListener("printerStatus", (event) => {
        listener({ type: "status", ...(event as Omit<Extract<PrinterNativeEvent, { type: "status" }>, "type">) });
      }),
      this.module.addListener("printerOperation", (event) => {
        listener({ type: "operation", ...(event as Omit<Extract<PrinterNativeEvent, { type: "operation" }>, "type">) });
      }),
      this.module.addListener("printerError", (event) => {
        listener({ type: "error", ...(event as Omit<Extract<PrinterNativeEvent, { type: "error" }>, "type">) });
      }),
    ];
    return () => subscriptions.forEach((subscription) => subscription.remove());
  }
}

/**
 * 延迟加载原生模块，使 iPad Simulator 与单元测试能注入 bridge；不可用时所有动作明确失败。
 */
export function createHbPrinterBridge(loader: NativeModuleLoader): HbPrinterBridge {
  try {
    return new ExpoHbPrinterBridge(loader());
  } catch {
    return new UnavailableHbPrinterBridge();
  }
}
