import assert from "node:assert/strict";
import Module from "node:module";
import React, { act } from "react";

function mockModule(name: string, exports: object) {
  const filename = require.resolve(name);
  const module = new Module(filename);
  module.filename = filename;
  module.loaded = true;
  module.exports = exports;
  require.cache[filename] = module;
}

async function run() {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const rendererReact = require.resolve("react", { paths: [require.resolve("test-renderer")] });
  if (rendererReact !== require.resolve("react")) mockModule(rendererReact, React);
  const { createRoot } = await import("test-renderer");
  const { usePrinterStore } = await import("./state");
  const savedPrinter = { address: "label", name: "XP label" };
  usePrinterStore.setState({ savedPrinter, hydrated: true, status: "connected", autoReconnectPaused: false });
  let nativeStatus = { supported: true, enabled: true, connected: true, address: "label" };
  let connects = 0;
  let connectFails = false;
  let now = 10_000;
  let interval: (() => void) | undefined;
  let nativeListener: (() => void) | undefined;
  let appListener: ((state: string) => void) | undefined;
  const originalInterval = globalThis.setInterval;
  const originalClearInterval = globalThis.clearInterval;
  const originalNow = Date.now;
  globalThis.setInterval = ((callback: () => void) => {
    interval = callback;
    return 1 as unknown as ReturnType<typeof setInterval>;
  }) as typeof setInterval;
  globalThis.clearInterval = (() => { interval = undefined; }) as typeof clearInterval;
  Date.now = () => now;
  const appState = {
    currentState: "active",
    addEventListener: (_name: string, listener: typeof appListener) => {
      appListener = listener;
      return { remove: () => { appListener = undefined; } };
    },
  };
  mockModule("react-native", { AppState: appState });
  mockModule("../../shared/i18n/i18n", { i18n: { t: (key: string) => key } });
  mockModule("./native", {
    subscribePrinterStatusChanged: (listener: () => void) => {
      nativeListener = listener;
      return () => { nativeListener = undefined; };
    },
  });
  mockModule("./api", {
    hydrateSavedPrinter: async () => savedPrinter,
    syncPrinterStatus: async () => {
      const store = usePrinterStore.getState();
      store.setStatus(store.autoReconnectPaused ? "paused" : nativeStatus.connected ? "connected" : "disconnected");
      return { ...nativeStatus };
    },
    connectSavedPrinter: async () => {
      connects++;
      if (connectFails) throw new Error("Printer is offline");
      nativeStatus.connected = true;
      usePrinterStore.getState().setStatus("connected");
      return true;
    },
  });
  const { usePrinterAutoConnect } = await import("./use-printer-auto-connect");
  function Harness() {
    usePrinterAutoConnect();
    return null;
  }
  const root = createRoot();
  try {
    await act(async () => { root.render(React.createElement(Harness)); });
    assert.ok(nativeListener, "挂载后必须订阅原生连接状态事件");
    assert.equal(connects, 0, "已连接时不能重新建立 socket");

    await act(async () => {
      nativeStatus.connected = false;
      nativeListener?.();
    });
    assert.equal(connects, 1, "断线事件应立即发起重连，无需等待五秒轮询");
    assert.equal(usePrinterStore.getState().status, "connected");

    await act(async () => { usePrinterStore.getState().setAutoReconnectPaused(true); });
    await act(async () => { nativeStatus.connected = false; nativeListener?.(); });
    assert.equal(connects, 1, "暂停时只能同步状态，不能自动连接");
    assert.equal(usePrinterStore.getState().status, "paused");

    await act(async () => {
      nativeStatus.enabled = false;
      usePrinterStore.getState().setAutoReconnectPaused(false);
      nativeListener?.();
    });
    assert.equal(connects, 1, "蓝牙关闭时不能反复请求连接");

    await act(async () => {
      appState.currentState = "background";
      appListener?.("background");
      nativeStatus.enabled = true;
      nativeListener?.();
      interval?.();
    });
    assert.equal(connects, 1, "后台不主动重连");
    now += 5_000;
    await act(async () => {
      appState.currentState = "active";
      appListener?.("active");
    });
    assert.equal(connects, 2, "回到前台立即检查连接");

    // 旧 APK 没有状态事件，轮询仍需发现断线；连接失败不能因状态更新形成热循环。
    now += 5_000;
    await act(async () => {
      nativeStatus.connected = false;
      connectFails = true;
      interval?.();
    });
    assert.equal(connects, 3);
    await act(async () => { nativeListener?.(); nativeListener?.(); interval?.(); });
    assert.equal(connects, 3, "同一重试间隔内的重复事件不能形成连接风暴");
    now += 5_000;
    await act(async () => { connectFails = false; interval?.(); });
    assert.equal(connects, 4);
    assert.equal(usePrinterStore.getState().status, "connected");
  } finally {
    await act(async () => { root.unmount(); });
    globalThis.setInterval = originalInterval;
    globalThis.clearInterval = originalClearInterval;
    Date.now = originalNow;
  }
  assert.equal(nativeListener, undefined, "卸载应取消原生事件监听");
  assert.equal(appListener, undefined, "卸载应取消前后台监听");
  assert.equal(interval, undefined, "卸载应取消轮询");
  console.log("use-printer-auto-connect.test.ts: ok");
}

void run();
