import { useEffect, useRef } from "react";
import { AppState, type AppStateStatus } from "react-native";

/**
 * 前台定时补查更新的间隔（30 分钟）。
 * 更新检查原本只在启动和「后台 → 前台」时触发；门店 Zebra 等设备整天停在前台，
 * 新 APK / OTA 发布后可能一整天都不提示，所以前台期间按固定间隔补查。
 * 各更新 hook 对同一安装包 / OTA 目标每次运行只提示一次，补查不会反复打扰已选「稍后」的用户。
 */
export const FOREGROUND_UPDATE_CHECK_INTERVAL_MS = 30 * 60 * 1000;

type IntervalHandle = ReturnType<typeof setInterval>;

export type ForegroundUpdateIntervalTimers = Readonly<{
  setInterval: (callback: () => void, delayMs: number) => IntervalHandle;
  clearInterval: (handle: IntervalHandle) => void;
}>;

const defaultTimers: ForegroundUpdateIntervalTimers = Object.freeze({
  // 调用时再取全局定时器，测试可以整体替换。
  setInterval: (callback: () => void, delayMs: number) => setInterval(callback, delayMs),
  clearInterval: (handle: IntervalHandle) => clearInterval(handle),
});

export type ForegroundUpdateInterval = Readonly<{
  sync: (state: AppStateStatus) => void;
  dispose: () => void;
}>;

/**
 * 按 AppState 启停补查计时：只在前台计时，离开前台立即停止。
 * 每次进入前台都重新计时——回到前台时各更新 hook 已立即检查过一次，不必紧接着再补查。
 */
export function createForegroundUpdateInterval(options: Readonly<{
  onTick: () => void;
  intervalMs?: number;
  timers?: ForegroundUpdateIntervalTimers;
}>): ForegroundUpdateInterval {
  const intervalMs = options.intervalMs ?? FOREGROUND_UPDATE_CHECK_INTERVAL_MS;
  const timers = options.timers ?? defaultTimers;
  let handle: IntervalHandle | null = null;
  let disposed = false;

  function stop() {
    if (handle !== null) {
      timers.clearInterval(handle);
      handle = null;
    }
  }

  return Object.freeze({
    sync(state: AppStateStatus) {
      stop();
      if (!disposed && state === "active") {
        handle = timers.setInterval(options.onTick, intervalMs);
      }
    },
    dispose() {
      disposed = true;
      stop();
    },
  });
}

/**
 * 在更新 hook 内启用前台定时补查。独立注册 AppState 监听，不改动各 hook 既有的立即检查逻辑；
 * onTick 总是调用最新一次渲染传入的回调，禁用（开发包、审核模式等）时不创建计时器。
 */
export function useForegroundUpdateCheckInterval(
  onTick: () => void,
  options: Readonly<{ enabled: boolean; intervalMs?: number }>,
) {
  const onTickRef = useRef(onTick);
  onTickRef.current = onTick;
  const intervalMs = options.intervalMs ?? FOREGROUND_UPDATE_CHECK_INTERVAL_MS;

  useEffect(() => {
    if (!options.enabled) {
      return;
    }
    const interval = createForegroundUpdateInterval({
      onTick: () => onTickRef.current(),
      intervalMs,
    });
    interval.sync(AppState.currentState);
    const subscription = AppState.addEventListener("change", (nextState) => {
      interval.sync(nextState);
    });
    return () => {
      subscription.remove();
      interval.dispose();
    };
  }, [options.enabled, intervalMs]);
}
