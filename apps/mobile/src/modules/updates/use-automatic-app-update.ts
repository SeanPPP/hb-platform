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
  const optionsRef = useRef(options);
  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const controllerRef = useRef(
    createAutomaticAppUpdateController({
      checkAndDownload: checkAndDownloadAppUpdate,
      promptRestart: ({ beforeApply }) => {
        const applyUpdate = createAutomaticAppUpdateApplyHandler({
          beforeApply: async () => (
            optionsRef.current.enabled && await beforeApply()
          ),
          apply: reloadAppToApplyUpdate,
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

  // 前置原生检查完成后 React 可能已提交新门禁；同步更新 ref，避免读取旧 render。
  optionsRef.current = options;

  useEffect(() => {
    if (!options.enabled) {
      return;
    }

    // 启动准备完成后执行一次自动检查；控制器会处理并发与重复提示。
    void controllerRef.current.check(optionsRef.current);
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
