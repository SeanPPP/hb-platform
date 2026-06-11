import { useEffect, useRef } from "react";
import { Alert, AppState, type AppStateStatus } from "react-native";
import { i18n } from "@/shared/i18n/i18n";
import { createAutomaticAppUpdateController } from "./automatic-app-update";
import { checkAndDownloadAppUpdate, reloadAppToApplyUpdate } from "./app-update-runtime";

export function useAutomaticAppUpdate(options: { enabled: boolean }) {
  const optionsRef = useRef(options);
  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const controllerRef = useRef(
    createAutomaticAppUpdateController({
      checkAndDownload: checkAndDownloadAppUpdate,
      promptRestart: () => {
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
                void reloadAppToApplyUpdate();
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

  useEffect(() => {
    optionsRef.current = options;
  }, [options.enabled]);

  useEffect(() => {
    if (!options.enabled) {
      return;
    }

    // 启动准备完成后执行一次自动检查；控制器会处理并发与重复提示。
    void controllerRef.current.check(options);
  }, [options.enabled]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      const previousState = appStateRef.current;
      appStateRef.current = nextState;

      // App 从后台回到前台时再检查一次，让长时间运行的门店设备也能拿到更新。
      void controllerRef.current.handleAppStateChange(previousState, nextState, optionsRef.current);
    });

    return () => {
      subscription.remove();
    };
  }, []);
}
