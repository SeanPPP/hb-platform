import { useEffect } from "react";

import { usePosShellStore } from "./pos-shell-store";

import { externalDisplay } from "@/core/peripherals/customer-display/native";

/**
 * 启动时导入客显模块以注册第二个 AppRegistry root，并只把外设状态投影到 UI 壳。
 * 外屏失败是非致命状态，不能向主收银流程抛错。
 */
export function PeripheralStatusBridge() {
  const setDisplay = usePosShellStore((state) => state.setDisplay);

  useEffect(() => {
    let active = true;

    const unsubscribe = externalDisplay.subscribe((status) => {
      if (active) {
        setDisplay(status);
      }
    });

    void externalDisplay
      .setEnabled(true)
      .then(() => externalDisplay.getStatus())
      .then((status) => {
        if (active) {
          setDisplay(status);
        }
      })
      .catch(() => {
        if (active) {
          setDisplay("failed");
        }
      });

    return () => {
      active = false;
      unsubscribe();
    };
  }, [setDisplay]);

  return null;
}
