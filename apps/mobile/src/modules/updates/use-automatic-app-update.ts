import { useEffect, useRef } from "react";
import { Alert, AppState, type AppStateStatus } from "react-native";
import { i18n } from "@/shared/i18n/i18n";
import {
  createAutomaticAppUpdateApplyHandler,
  createAutomaticAppUpdateController,
  type AutomaticAppUpdateOptions,
} from "./automatic-app-update";
import { checkAndDownloadAppUpdate, reloadAppToApplyUpdate } from "./app-update-runtime";

export function useAutomaticAppUpdate(options: AutomaticAppUpdateOptions) {
  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const controllerRef = useRef(
    createAutomaticAppUpdateController({
      checkAndDownload: checkAndDownloadAppUpdate,
      promptRestart: ({ beforeApply, isCurrent }) => {
        const applyUpdate = createAutomaticAppUpdateApplyHandler({
          beforeApply,
          isCurrent,
          apply: () => reloadAppToApplyUpdate({ isCurrent }),
          warn: (error) => {
            console.warn("[updates] automatic OTA apply failed", error);
          },
        });
        Alert.alert(
          i18n.t("settings:dialogs.autoUpdateReadyTitle"),
          i18n.t("settings:dialogs.autoUpdateReadyMessage"),
          [
            {
              text: i18n.t("settings:dialogs.autoUpdateLaterAction"),
              style: "cancel",
            },
            {
              text: i18n.t("settings:dialogs.autoUpdateRestartAction"),
              onPress: () => {
                void applyUpdate();
              },
            },
          ]
        );
      },
      warn: (error) => {
        console.warn("[updates] automatic OTA check failed", error);
      },
    })
  );

  // 控制器跨 render 存活，异步边界始终读取这一份 live options。
  controllerRef.current.updateOptions(options);

  useEffect(() => {
    if (!options.enabled) {
      controllerRef.current.cancel();
      return;
    }

    // 启动准备完成后执行一次自动检查；控制器会处理并发与重复提示。
    void controllerRef.current.check();

    return () => {
      controllerRef.current.cancel();
    };
  }, [options.enabled]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      const previousState = appStateRef.current;
      appStateRef.current = nextState;

      // App 从后台回到前台时再检查一次，让长时间运行的门店设备也能拿到更新。
      void controllerRef.current.handleAppStateChange(previousState, nextState);
    });

    return () => {
      subscription.remove();
      controllerRef.current.cancel();
    };
  }, []);
}
