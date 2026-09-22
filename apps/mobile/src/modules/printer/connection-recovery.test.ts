import assert from "node:assert/strict";
import Module from "node:module";
import { beforeEach, test } from "node:test";
import type { PrinterStatus, SavedPrinter } from "./types";

function mockModule(name: string, exports: object) {
  const filename = require.resolve(name);
  const module = new Module(filename);
  module.filename = filename;
  module.loaded = true;
  module.exports = exports;
  require.cache[filename] = module;
}

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((done) => { resolve = done; });
  return { promise, resolve };
}

async function run() {
  let saved: SavedPrinter | null;
  let nativeStatus: PrinterStatus;
  let events: string[];
  let writeError: Error | null;
  let connectError: Error | null;
  let disconnectError: Error | null;
  let statusReads: number;
  let storageReads: number;
  let printGate: ReturnType<typeof deferred> | null;
  let connectGate: ReturnType<typeof deferred> | null;
  let reviewMode = false;
  const receipt = { name: "Receipt", address: "receipt" };

  const print = async () => {
    events.push(`print:${nativeStatus.address}`);
    await printGate?.promise;
    if (writeError) throw writeError;
    return true;
  };

  // 仅替换蓝牙与存储边界，实际执行共享 API 和 Zustand 状态转换。
  mockModule("./native", {
    getPrinterStatus: async () => { statusReads += 1; return { ...nativeStatus }; },
    connectPrinter: async (address: string) => {
      events.push(`connect:${address}`);
      await connectGate?.promise;
      if (connectError) throw connectError;
      nativeStatus = { ...nativeStatus, connected: true, address };
      return true;
    },
    disconnectPrinter: async () => {
      events.push("disconnect");
      if (disconnectError) throw disconnectError;
      nativeStatus = { ...nativeStatus, connected: false, address: null };
      return true;
    },
    printNativeProductLabel: print,
    printNativeDiscountLabel: print,
    printNativeClearanceLabel: print,
    printNativeBigDiscountLabel: print,
    printNativeWarehouseProductLabel: print,
    printNativeWarehouseLocationLabel: print,
    printRawCommand: print,
  });
  mockModule("./storage", {
    PrinterStorage: {
      getPrinter: async () => { storageReads += 1; return saved; },
      setPrinter: async (printer: SavedPrinter) => { saved = printer; },
      clearPrinter: async () => { saved = null; },
      getReceiptPrinter: async () => receipt,
    },
  });
  mockModule("../ios-review/session", { isIosReviewSessionActive: () => reviewMode });
  const api = await import("./api");
  const { usePrinterStore, useReceiptPrinterStore } = await import("./state");
  const payload = { productName: "Test label", barcode: "1234567890128" };

  beforeEach(() => {
    saved = { name: "XP label", address: "label" };
    nativeStatus = { supported: true, enabled: true, connected: true, address: "label" };
    events = [];
    writeError = null;
    connectError = null;
    disconnectError = null;
    statusReads = 0;
    storageReads = 0;
    printGate = null;
    connectGate = null;
    reviewMode = false;
    usePrinterStore.setState({ savedPrinter: saved, status: "connected", autoReconnectPaused: false, lastError: null, hydrated: true });
    useReceiptPrinterStore.setState({ savedPrinter: receipt, status: "idle", autoReconnectPaused: false, lastError: null, hydrated: true });
  });

  test("broken pipe 清除假连接，保留原始失败且不自动重印，下一次打印恢复", async () => {
    writeError = new Error("write failed: EPIPE (Broken pipe)");
    const original = writeError;
    await assert.rejects(api.printProductLabelPayload(payload), (error) => error === original);
    assert.deepEqual(events, ["print:label", "disconnect"]);
    assert.equal(usePrinterStore.getState().status, "disconnected");
    assert.equal(usePrinterStore.getState().lastError, original.message);
    writeError = null;
    await api.printProductLabelPayload(payload);
    assert.deepEqual(events, ["print:label", "disconnect", "connect:label", "print:label"]);
    assert.equal(usePrinterStore.getState().status, "connected");
  });

  test("已 hydration 的热连接打印复用内存地址，只读取一次原生状态", async () => {
    assert.equal((await api.getSavedPrinter())?.address, "label");
    await api.printProductLabelPayload(payload);
    assert.equal(statusReads, 1);
    assert.equal(storageReads, 0);
    assert.deepEqual(events, ["print:label"]);
  });

  test("切换标签打印机时先更新内存地址，再连接并向新设备打印", async () => {
    await api.selectPrinter({ name: "Other", address: "other", bonded: true, connected: false });
    assert.equal((await api.getSavedPrinter())?.address, "other");
    await api.printProductLabelPayload(payload);
    assert.deepEqual(events, ["disconnect", "connect:other", "print:other"]);
    assert.equal(storageReads, 0);
  });

  test("旧 iOS 原生包写入超时后也丢弃会话，重试前重新连接且不自动重印", async () => {
    writeError = Object.assign(new Error("Bluetooth printer write timed out."), { code: "PRINT_TIMEOUT" });
    const original = writeError;
    await assert.rejects(api.printProductLabelPayload(payload), (error) => error === original);
    assert.deepEqual(events, ["print:label", "disconnect"]);
    assert.equal(usePrinterStore.getState().status, "disconnected");
    writeError = null;
    await api.printProductLabelPayload(payload);
    assert.deepEqual(events, ["print:label", "disconnect", "connect:label", "print:label"]);
  });

  test("标签内容错误不误判为断线或重连", async () => {
    writeError = new Error("Barcode content is invalid");
    await assert.rejects(api.printProductLabelPayload(payload), /Barcode content/);
    assert.deepEqual(events, ["print:label"]);
    assert.equal(usePrinterStore.getState().status, "connected");
  });

  test("价签更新的折扣标签断线后也清除旧连接，下一次打印重连", async () => {
    writeError = new Error("write failed: EPIPE (Broken pipe)");
    await assert.rejects(api.printDiscountLabelPayload(payload), /Broken pipe/);
    assert.deepEqual(events, ["print:label", "disconnect"]);
    assert.equal(usePrinterStore.getState().status, "disconnected");
    writeError = null;
    await api.printDiscountLabelPayload(payload);
    assert.deepEqual(events, ["print:label", "disconnect", "connect:label", "print:label"]);
    assert.equal(usePrinterStore.getState().status, "connected");
  });

  test("清理失败也不能恢复假连接状态，自动连接仍会建立新 socket", async () => {
    writeError = new Error("Broken pipe");
    disconnectError = new Error("Disconnect failed");
    await assert.rejects(api.printProductLabelPayload(payload), /Broken pipe/);
    assert.equal((await api.syncPrinterStatus()).connected, false);
    assert.equal(usePrinterStore.getState().status, "disconnected");
    disconnectError = null;
    writeError = null;
    await api.connectSavedPrinter();
    assert.equal((await api.syncPrinterStatus()).connected, true);
    assert.deepEqual(events, ["print:label", "disconnect", "connect:label"]);
  });

  test("自动重连与打印同时发生时只连接一次", async () => {
    nativeStatus.connected = false;
    connectGate = deferred();
    const reconnect = api.connectSavedPrinter({ status: "reconnecting" });
    const printing = api.printProductLabelPayload(payload);
    await new Promise<void>((resolve) => setImmediate(resolve));
    const eventsBeforeConnected = [...events];
    connectGate.resolve();
    await Promise.all([reconnect, printing]);
    assert.deepEqual(eventsBeforeConnected, ["connect:label"]);
    assert.deepEqual(events, ["connect:label", "print:label"]);
  });

  test("小票测试等待标签写入完成，之后标签打印重新连接正确设备", async () => {
    printGate = deferred();
    const printing = api.printProductLabelPayload(payload);
    await new Promise<void>((resolve) => setImmediate(resolve));
    const receiptTest = api.testReceiptPrinterConnection();
    await new Promise<void>((resolve) => setImmediate(resolve));
    const eventsDuringPrint = [...events];
    printGate.resolve();
    await Promise.all([printing, receiptTest]);
    assert.deepEqual(eventsDuringPrint, ["print:label"]);
    assert.deepEqual(events, ["print:label", "connect:receipt", "print:receipt", "disconnect"]);
    assert.equal(usePrinterStore.getState().autoReconnectPaused, false);
    await api.printProductLabelPayload(payload);
    assert.deepEqual(events.slice(-2), ["connect:label", "print:label"]);
  });

  test("排队中的自动重连不会覆盖用户暂停", async () => {
    printGate = deferred();
    const printing = api.printProductLabelPayload(payload);
    await new Promise<void>((resolve) => setImmediate(resolve));
    const reconnect = api.connectSavedPrinter({ status: "reconnecting" });
    const disconnect = api.disconnectCurrentPrinter({ pauseAutoReconnect: true });
    printGate.resolve();
    await Promise.all([printing, reconnect, disconnect]);
    assert.equal(events.filter((event) => event.startsWith("connect:")).length, 0);
    assert.equal(usePrinterStore.getState().autoReconnectPaused, true);
    assert.equal(usePrinterStore.getState().status, "paused");
  });

  test("小票测试中手动暂停，测试结束不能恢复之前的自动重连意图", async () => {
    printGate = deferred();
    const receiptTest = api.testReceiptPrinterConnection();
    await new Promise<void>((resolve) => setImmediate(resolve));
    const disconnect = api.disconnectCurrentPrinter({ pauseAutoReconnect: true });
    printGate.resolve();
    await Promise.all([receiptTest, disconnect]);
    assert.equal(usePrinterStore.getState().autoReconnectPaused, true);
    assert.equal(usePrinterStore.getState().status, "paused");
  });

  test("重连失败释放操作队列并更新错误，恢复后可重连", async () => {
    nativeStatus.connected = false;
    connectError = new Error("Connection timed out");
    await assert.rejects(api.connectSavedPrinter(), /Connection timed out/);
    assert.equal(usePrinterStore.getState().status, "error");
    assert.equal(usePrinterStore.getState().lastError, connectError.message);
    connectError = null;
    await api.connectSavedPrinter();
    assert.equal(usePrinterStore.getState().status, "connected");
  });

  test("审核模式保持模拟打印，不读写真实蓝牙", async () => {
    reviewMode = true;
    await api.printProductLabelPayload(payload);
    await api.connectSavedPrinter();
    await api.testReceiptPrinterConnection();
    assert.deepEqual(events, []);
  });
}

void run();
