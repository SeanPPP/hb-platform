import assert from "node:assert/strict";

import type {
  BluetoothPrinterStatus,
  HbPrinterBridge,
  PrinterNativeEvent,
  PrinterOperationKind,
  PrinterOperationResult,
} from "./types";

import {
  createHbPrinterBridge,
  HbPrinterNativeAdapter,
  PrinterNativeUnavailableError,
  PrinterOutcomeUnknownError,
} from "./index";

const readyStatus: BluetoothPrinterStatus = {
  supported: true,
  enabled: true,
  connection: "ready",
  peripheralId: "A0B1C2D3-E4F5-6789-ABCD-EF0123456789",
  writeMode: "withResponse",
};

class FakeBridge implements HbPrinterBridge {
  lastOperation: { id: string; kind: PrinterOperationKind; bytes?: number[] } | null = null;
  connectCalls = 0;
  connectError: Error | null = null;
  nextResult: PrinterOperationResult = {
    operationId: "operation-1",
    state: "completed",
    message: "BLE 命令已传输到打印机。",
  };

  async getStatus(): Promise<BluetoothPrinterStatus> {
    return readyStatus;
  }

  async scan() {
    return [
      {
        id: readyStatus.peripheralId ?? "",
        name: "Xprinter XP-58",
        rssi: -42,
        isXprinter: true,
      },
    ];
  }

  async connect(): Promise<BluetoothPrinterStatus> {
    this.connectCalls += 1;
    if (this.connectError) {
      throw this.connectError;
    }
    return readyStatus;
  }

  async disconnect(): Promise<BluetoothPrinterStatus> {
    return { ...readyStatus, connection: "disconnected", peripheralId: null };
  }

  async write(
    operationId: string,
    bytes: number[],
    _timeoutMs: number,
    kind: PrinterOperationKind,
  ): Promise<PrinterOperationResult> {
    this.lastOperation = { id: operationId, kind, bytes };
    return { ...this.nextResult, operationId };
  }

  async printText(operationId: string): Promise<PrinterOperationResult> {
    this.lastOperation = { id: operationId, kind: "print" };
    return { ...this.nextResult, operationId };
  }

  async openCashDrawer(operationId: string): Promise<PrinterOperationResult> {
    this.lastOperation = { id: operationId, kind: "drawer" };
    return { ...this.nextResult, operationId };
  }

  subscribe(_listener: (event: PrinterNativeEvent) => void): () => void {
    return () => undefined;
  }
}

async function run(): Promise<void> {
  const fakeBridge = new FakeBridge();
  const adapter = new HbPrinterNativeAdapter(fakeBridge);

  const printers = await adapter.scan();
  assert.equal(printers.length, 1);
  assert.equal(printers[0]?.name, "Xprinter XP-58");
  assert.equal(await adapter.getStatus(), "ready");

  // 原生连接被主动取消或意外中断时，Promise 必须立即向上游失败，不能让 UI 一直等待。
  fakeBridge.connectError = new Error("蓝牙打印机连接已取消。");
  await assert.rejects(() => adapter.connect(readyStatus.peripheralId!, 12_000), /连接已取消/);
  assert.equal(fakeBridge.connectCalls, 1);
  fakeBridge.connectError = null;

  await adapter.printRaw("receipt-1", new Uint8Array([0x1b, 0x40]));
  assert.deepEqual(fakeBridge.lastOperation, {
    id: "receipt-1",
    kind: "print",
    bytes: [0x1b, 0x40],
  });

  const printed = await adapter.print("receipt-port-1", new Uint8Array([0x1d, 0x56, 0x00]));
  assert.deepEqual(printed, { status: "printed", errorCode: null });

  // 原生层拒绝空命令时必须稳定映射为失败，不能把未写入蓝牙外设的操作误报为成功。
  fakeBridge.nextResult = {
    operationId: "empty-print-1",
    state: "failed",
    message: "空打印命令被拒绝。",
  };
  const failedPrint = await adapter.print("empty-print-1", new Uint8Array());
  assert.deepEqual(failedPrint, {
    status: "failed",
    errorCode: "PRINTER_OPERATION_FAILED",
  });

  fakeBridge.nextResult = {
    operationId: "drawer-1",
    state: "unknown",
    message: "BLE 写入超时。",
  };
  const ambiguousPrint = await adapter.print("receipt-port-2", new Uint8Array([0x0a]));
  assert.deepEqual(ambiguousPrint, {
    status: "ambiguous",
    errorCode: "PRINTER_OUTCOME_UNKNOWN",
  });
  const unknownDrawer = await adapter.open("drawer-port-1");
  assert.deepEqual(unknownDrawer, {
    status: "unknown",
    errorCode: "PRINTER_OUTCOME_UNKNOWN",
  });
  await assert.rejects(
    () => adapter.openCashDrawer({ operationId: "drawer-1" }),
    (error: unknown) => error instanceof PrinterOutcomeUnknownError,
  );
  assert.equal(fakeBridge.lastOperation?.kind, "drawer");

  const unavailable = createHbPrinterBridge(() => {
    throw new Error("native module is absent on simulator");
  });
  const unavailableStatus = await unavailable.getStatus();
  assert.equal(unavailableStatus.supported, false);
  await assert.rejects(() => unavailable.scan(5_000, false), PrinterNativeUnavailableError);
  assert.equal(await new HbPrinterNativeAdapter(unavailable).getStatus(), "unavailable");

  await assert.rejects(() => adapter.connect("", 12_000), /peripheralId 不能为空/);
  await assert.rejects(() => adapter.scan({ durationMs: 1_000 }), /timeoutMs/);
}

void run();
