import { useEffect, useMemo, useRef } from "react";
import { AppState, type AppStateStatus } from "react-native";

import { usePosShellStore } from "./pos-shell-store";
import { RuntimeWorkController } from "./runtime-work-controller";

import { usePosRuntime } from "@/core/runtime/pos-runtime-context";

export function RuntimeWorkBridge() {
  const runtime = usePosRuntime();
  const connectivity = usePosShellStore((state) => state.connectivity);
  const lastAppState = useRef<AppStateStatus>(AppState.currentState);
  const controller = useMemo(
    () =>
      runtime.services
        ? new RuntimeWorkController(runtime.services)
        : null,
    [runtime.services],
  );

  useEffect(() => {
    if (!controller) return undefined;
    void controller.onApplicationStarted().catch(() => {
      // 同步/外设队列保留了失败事实；后台触发器不能让 React 树崩溃。
    });
    return undefined;
  }, [controller]);

  useEffect(() => {
    if (!controller || connectivity === "checking") return undefined;
    void controller
      .onNetworkChanged(connectivity === "online")
      .catch(() => {
        // 网络恢复失败由 outbox 退避，不能在 UI 生命周期中强制重试。
      });
    return undefined;
  }, [connectivity, controller]);

  useEffect(() => {
    if (!controller) return undefined;
    const subscription = AppState.addEventListener("change", (next) => {
      const becameActive =
        next === "active" && lastAppState.current !== "active";
      lastAppState.current = next;
      if (becameActive) {
        void controller.onForeground().catch(() => {
          // 前台恢复失败保持队列现状，主管可从同步/履约历史继续处理。
        });
      }
    });
    return () => subscription.remove();
  }, [controller]);

  return null;
}
