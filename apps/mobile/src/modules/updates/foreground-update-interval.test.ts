import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import Module from "node:module";
import { join } from "node:path";
import React, { act } from "react";

function mockModule(name: string, exports: object) {
  const filename = require.resolve(name);
  const module = new Module(filename);
  module.filename = filename;
  module.loaded = true;
  module.exports = exports;
  require.cache[filename] = module;
}

type FakeInterval = { callback: () => void; delayMs: number };

function createFakeTimers() {
  let nextHandle = 1;
  const active = new Map<number, FakeInterval>();
  return {
    active,
    timers: {
      setInterval: (callback: () => void, delayMs: number) => {
        const handle = nextHandle++;
        active.set(handle, { callback, delayMs });
        return handle as unknown as ReturnType<typeof setInterval>;
      },
      clearInterval: (handle: ReturnType<typeof setInterval>) => {
        active.delete(handle as unknown as number);
      },
    },
    // 模拟「过了一个间隔」：触发当前所有仍在计时的回调。
    elapse: () => {
      for (const interval of [...active.values()]) interval.callback();
    },
  };
}

async function run() {
  let appListener: ((state: string) => void) | undefined;
  const appState = {
    currentState: "active",
    addEventListener: (_name: string, listener: (state: string) => void) => {
      appListener = listener;
      return { remove: () => { appListener = undefined; } };
    },
  };
  mockModule("react-native", { AppState: appState });

  const {
    FOREGROUND_UPDATE_CHECK_INTERVAL_MS,
    createForegroundUpdateInterval,
    useForegroundUpdateCheckInterval,
  } = await import("./foreground-update-interval");

  assert.equal(FOREGROUND_UPDATE_CHECK_INTERVAL_MS, 30 * 60 * 1000, "前台补查间隔为 30 分钟");

  // 一、纯计时控制器
  {
    const fake = createFakeTimers();
    let ticks = 0;
    const interval = createForegroundUpdateInterval({ onTick: () => { ticks++; }, timers: fake.timers });

    interval.sync("active");
    assert.equal(fake.active.size, 1, "进入前台开始计时");
    assert.equal([...fake.active.values()][0].delayMs, FOREGROUND_UPDATE_CHECK_INTERVAL_MS);
    fake.elapse();
    assert.equal(ticks, 1, "每个间隔补查一次");

    interval.sync("background");
    assert.equal(fake.active.size, 0, "进入后台立即停止计时");
    fake.elapse();
    assert.equal(ticks, 1, "后台不补查");

    interval.sync("active");
    interval.sync("active");
    assert.equal(fake.active.size, 1, "重复进入前台只保留一个计时器（重新计时）");

    interval.sync("inactive");
    assert.equal(fake.active.size, 0, "iOS inactive 也视为离开前台");

    interval.sync("active");
    interval.dispose();
    assert.equal(fake.active.size, 0, "释放时清理计时器");
    interval.sync("active");
    assert.equal(fake.active.size, 0, "释放后不能再启动");

    const custom = createForegroundUpdateInterval({ onTick: () => undefined, intervalMs: 5_000, timers: fake.timers });
    custom.sync("active");
    assert.equal([...fake.active.values()][0].delayMs, 5_000, "支持自定义间隔");
    custom.dispose();
  }

  // 二、hook 挂载行为（模拟 AppState 与全局定时器）
  const intervals = new Map<number, FakeInterval>();
  let nextHandle = 100;
  const originalSetInterval = globalThis.setInterval;
  const originalClearInterval = globalThis.clearInterval;
  globalThis.setInterval = ((callback: () => void, delayMs: number) => {
    const handle = nextHandle++;
    intervals.set(handle, { callback, delayMs });
    return handle as unknown as ReturnType<typeof setInterval>;
  }) as typeof setInterval;
  globalThis.clearInterval = ((handle: number) => { intervals.delete(handle); }) as typeof clearInterval;

  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const rendererReact = require.resolve("react", { paths: [require.resolve("test-renderer")] });
  if (rendererReact !== require.resolve("react")) mockModule(rendererReact, React);
  const { createRoot } = await import("test-renderer");

  const calls: string[] = [];
  function Harness(props: { label: string; enabled: boolean }) {
    useForegroundUpdateCheckInterval(() => { calls.push(props.label); }, { enabled: props.enabled });
    return null;
  }
  const elapse = () => { for (const interval of [...intervals.values()]) interval.callback(); };

  const root = createRoot();
  try {
    await act(async () => { root.render(React.createElement(Harness, { label: "first", enabled: true })); });
    assert.equal(intervals.size, 1, "挂载时处于前台即开始计时");
    assert.equal([...intervals.values()][0].delayMs, FOREGROUND_UPDATE_CHECK_INTERVAL_MS);
    assert.ok(appListener, "挂载后监听前后台切换");

    await act(async () => { root.render(React.createElement(Harness, { label: "latest", enabled: true })); });
    assert.equal(intervals.size, 1, "重新渲染不重建计时器");
    elapse();
    assert.deepEqual(calls, ["latest"], "补查总是调用最新一次渲染的检查函数");

    await act(async () => { appListener?.("background"); });
    assert.equal(intervals.size, 0, "进入后台停止计时");
    await act(async () => { appListener?.("active"); });
    assert.equal(intervals.size, 1, "回到前台重新计时");

    await act(async () => { root.render(React.createElement(Harness, { label: "latest", enabled: false })); });
    assert.equal(intervals.size, 0, "禁用时清理计时器");
    assert.equal(appListener, undefined, "禁用时移除前后台监听");

    appState.currentState = "background";
    await act(async () => { root.render(React.createElement(Harness, { label: "latest", enabled: true })); });
    assert.equal(intervals.size, 0, "在后台启用时先不计时");
    await act(async () => { appState.currentState = "active"; appListener?.("active"); });
    assert.equal(intervals.size, 1, "等回到前台再开始计时");
  } finally {
    await act(async () => { root.unmount(); });
    globalThis.setInterval = originalSetInterval;
    globalThis.clearInterval = originalClearInterval;
  }
  assert.equal(intervals.size, 0, "卸载清理计时器");
  assert.equal(appListener, undefined, "卸载移除前后台监听");

  // 三、三条更新链路都接入前台补查，且沿用各自的检查入口与启用条件
  const source = (file: string) => readFileSync(join(__dirname, file), "utf8");
  assert.match(
    source("use-automatic-native-app-update.ts"),
    /useForegroundUpdateCheckInterval\(\(\) => \{\s*void check\(optionsRef\.current\);\s*\}, \{ enabled: options\.enabled \}\)/,
    "Android APK 更新接入前台补查",
  );
  assert.match(
    source("use-mobile-ota-update.ts"),
    /useForegroundUpdateCheckInterval\(\(\) => \{\s*void runCheckRef\.current\(\);\s*\}, \{ enabled: effectiveEnabled \}\)/,
    "Mobile OTA 接入前台补查",
  );
  assert.match(
    source("use-ios-native-app-update.ts"),
    /useForegroundUpdateCheckInterval\(\(\) => \{\s*if \(enabledRef\.current\) \{\s*void runServerCheckRef\.current\(\);\s*\}\s*\}, \{ enabled: options\.enabled \}\)/,
    "iOS App Store 更新接入前台补查",
  );

  console.log("foreground-update-interval.test.ts: ok");
}

void run();
