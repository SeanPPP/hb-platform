import { useEffect, useRef, useState } from "react";
import Constants from "expo-constants";
import * as Application from "expo-application";
import { Alert, AppState, Linking, type AppStateStatus } from "react-native";
import { i18n } from "@/shared/i18n/i18n";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import {
  checkIosNativeAppUpdate,
  createIosNativeOptionalReminderSession,
  markIosNativeOptionalReminder,
  readCachedIosNativeRequiredDecision,
  shouldActivateIosNativeOptionalPrompt,
  shouldCheckIosNativeUpdateOnAppStateChange,
  type IosNativeUpdateContext,
  type IosNativeUpdateDecision,
  type IosNativeUpdateCheckReceipt,
} from "./ios-native-app-update";
import { fetchIosNativeUpdateDecision } from "./ios-native-app-update-api";
import { resolveIosNativeUpdateCenterBaseUrl } from "./ios-native-update-center";

type IosNativeUpdateSnapshot = {
  initialized: boolean;
  decision: IosNativeUpdateDecision | null;
  optionalPromptActive: boolean;
};

const EMPTY_SNAPSHOT: IosNativeUpdateSnapshot = {
  initialized: false,
  decision: null,
  optionalPromptActive: false,
};

async function resolveUpdateContext(): Promise<IosNativeUpdateContext> {
  const apiBaseUrl = resolveIosNativeUpdateCenterBaseUrl({
    buildProfile: Constants.expoConfig?.extra?.nativeAppBuildProfile,
    override: Constants.expoConfig?.extra?.iosNativeUpdateCenterUrl,
  });
  const installedVersion =
    Application.nativeApplicationVersion
    ?? Constants.expoConfig?.version
    ?? "";
  const installedBuild =
    Application.nativeBuildVersion
    ?? Constants.expoConfig?.ios?.buildNumber
    ?? "";

  if (!installedVersion.trim() || !installedBuild.trim()) {
    throw new Error("当前 iOS 安装包缺少版本号或构建号");
  }

  return {
    apiBaseUrl,
    installedVersion,
    installedBuild,
  };
}

async function openAppStore(url: string) {
  try {
    await Linking.openURL(url);
  } catch (error) {
    console.warn("[updates] open iOS App Store failed", error);
    Alert.alert(
      i18n.t("settings:dialogs.iosNativeUpdateOpenFailedTitle"),
      i18n.t("settings:dialogs.iosNativeUpdateOpenFailedMessage"),
    );
  }
}

export function useIosNativeAppUpdate(options: { enabled: boolean }) {
  const [snapshot, setSnapshot] = useState<IosNativeUpdateSnapshot>(EMPTY_SNAPSHOT);
  const [checking, setChecking] = useState(false);
  const enabledRef = useRef(options.enabled);
  const snapshotRef = useRef(snapshot);
  const contextRef = useRef<IosNativeUpdateContext | null>(null);
  const generationRef = useRef(0);
  const updateEpochRef = useRef(0);
  const optionalReminderSessionRef = useRef(
    createIosNativeOptionalReminderSession(),
  );
  const appStateRef = useRef<AppStateStatus>(AppState.currentState);
  const inFlightRef = useRef<{
    generation: number;
    controller: AbortController;
    promise: Promise<IosNativeUpdateCheckReceipt | null>;
  } | null>(null);
  const runServerCheckRef = useRef<
    () => Promise<IosNativeUpdateCheckReceipt | null>
  >(async () => null);

  enabledRef.current = options.enabled;
  snapshotRef.current = snapshot;

  runServerCheckRef.current = async () => {
    const context = contextRef.current;
    const generation = generationRef.current;
    if (!enabledRef.current || !context) {
      return null;
    }

    if (inFlightRef.current?.generation === generation) {
      return inFlightRef.current.promise;
    }

    const epoch = updateEpochRef.current + 1;
    updateEpochRef.current = epoch;
    const controller = new AbortController();
    const isCurrent = () => (
      enabledRef.current
      && generationRef.current === generation
      && !controller.signal.aborted
    );
    setChecking(true);
    const promise = (async () => {
      try {
        const outcome = await checkIosNativeAppUpdate({
          context,
          storage: AppAsyncStorage,
          now: Date.now,
          signal: controller.signal,
          fetchDecision: (signal) =>
            fetchIosNativeUpdateDecision(context, undefined, signal),
          optionalReminderSession: optionalReminderSessionRef.current,
          // 同一 generation 已见 required 时，缓存写失败与后续离线都不能把门禁降级为首次放行。
          memoryRequiredDecision:
            snapshotRef.current.decision?.state === "required"
              ? snapshotRef.current.decision
              : null,
        });

        if (!isCurrent()) {
          return null;
        }

        const optionalPromptActive = shouldActivateIosNativeOptionalPrompt({
          decision: outcome.decision,
          shouldPromptOptional: outcome.shouldPromptOptional,
        });
        const nextSnapshot = {
          initialized: true,
          decision: outcome.decision,
          // 决策与弹窗占用状态必须同一批提交，不能在持久化提醒期间提前恢复 OTA。
          optionalPromptActive,
        };
        snapshotRef.current = nextSnapshot;
        setSnapshot(nextSnapshot);

        if (outcome.error) {
          console.warn("[updates] iOS App Store update check failed", outcome.error);
        }
        if (outcome.storageError) {
          console.warn("[updates] persist iOS App Store update state failed", outcome.storageError);
        }

        if (
          optionalPromptActive
          && outcome.decision?.state === "optional"
          && outcome.decision.appStoreUrl
        ) {
          // 先记录提醒时间，避免 AppState 短时间抖动造成重复弹窗。
          try {
            await markIosNativeOptionalReminder(
              AppAsyncStorage,
              context,
              outcome.decision,
              Date.now(),
            );
          } catch (error) {
            if (isCurrent()) {
              console.warn("[updates] persist iOS App Store reminder failed", error);
            }
          }
          if (!isCurrent()) {
            return null;
          }

          const decision = outcome.decision;
          // Alert 即将展示时先记入进程内会话；即使持久化失败，用户处理后也不会立刻再弹。
          optionalReminderSessionRef.current.markSeen(context, decision);
          const resolveOptionalPrompt = () => {
            if (!isCurrent()) {
              return false;
            }
            setSnapshot((current) => ({
              ...current,
              optionalPromptActive: false,
            }));
            snapshotRef.current = {
              ...snapshotRef.current,
              optionalPromptActive: false,
            };
            return true;
          };
          Alert.alert(
            i18n.t("settings:dialogs.iosNativeUpdateAvailableTitle"),
            decision.releaseMessage
              || i18n.t("settings:dialogs.iosNativeUpdateAvailableMessage", {
                version: decision.latestVersion,
              }),
            [
              {
                text: i18n.t("settings:dialogs.iosNativeUpdateLaterAction"),
                style: "cancel",
                onPress: resolveOptionalPrompt,
              },
              {
                text: i18n.t("settings:dialogs.iosNativeUpdateOpenStoreAction"),
                onPress: () => {
                  if (resolveOptionalPrompt()) {
                    void openAppStore(decision.appStoreUrl!);
                  }
                },
              },
            ],
          );
        }
        return { epoch, outcome };
      } catch (error) {
        if (!isCurrent()) {
          // AbortSignal 与 generation 失效统一返回 null；真实网络失败由 outcome receipt 表达。
          return null;
        }
        throw error;
      }
    })().finally(() => {
      if (
        inFlightRef.current?.generation === generation
        && inFlightRef.current.controller === controller
      ) {
        inFlightRef.current = null;
        setChecking(false);
      }
    });

    inFlightRef.current = { generation, controller, promise };
    return promise;
  };

  useEffect(() => {
    inFlightRef.current?.controller.abort();
    inFlightRef.current = null;
    const generation = generationRef.current + 1;
    generationRef.current = generation;
    // 构建资格切换也会使既有 OTA 检查凭据失效。
    updateEpochRef.current += 1;
    contextRef.current = null;
    setSnapshot(EMPTY_SNAPSHOT);
    setChecking(false);

    if (!options.enabled) {
      return;
    }

    void (async () => {
      try {
        const context = await resolveUpdateContext();
        if (!enabledRef.current || generationRef.current !== generation) {
          return;
        }
        contextRef.current = context;

        const cachedDecision = await readCachedIosNativeRequiredDecision(
          AppAsyncStorage,
          context,
        );
        if (!enabledRef.current || generationRef.current !== generation) {
          return;
        }

        if (cachedDecision) {
          const cachedSnapshot = {
            initialized: true,
            decision: cachedDecision,
            optionalPromptActive: false,
          };
          snapshotRef.current = cachedSnapshot;
          setSnapshot(cachedSnapshot);
        }

        await runServerCheckRef.current();
      } catch (error) {
        if (!enabledRef.current || generationRef.current !== generation) {
          return;
        }
        console.warn("[updates] initialize iOS App Store update check failed", error);
        // 没有可读取的可信强制缓存时，初始化失败按既定策略放行业务页面。
        const failedSnapshot = {
          initialized: true,
          decision: null,
          optionalPromptActive: false,
        };
        snapshotRef.current = failedSnapshot;
        setSnapshot(failedSnapshot);
      }
    })();

    return () => {
      if (generationRef.current === generation) {
        generationRef.current += 1;
        updateEpochRef.current += 1;
      }
      inFlightRef.current?.controller.abort();
      inFlightRef.current = null;
    };
  }, [options.enabled]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      const previousState = appStateRef.current;
      appStateRef.current = nextState;
      if (
        !enabledRef.current
        || !shouldCheckIosNativeUpdateOnAppStateChange(previousState, nextState)
      ) {
        return;
      }
      void runServerCheckRef.current();
    });

    return () => {
      subscription.remove();
    };
  }, []);

  return {
    ...snapshot,
    checking,
    openRequiredUpdate: () => {
      if (!enabledRef.current) {
        return;
      }
      const url = snapshotRef.current.decision?.appStoreUrl;
      if (url) {
        void openAppStore(url);
      }
    },
    recheck: () => runServerCheckRef.current(),
    getCheckEpoch: () => updateEpochRef.current,
  };
}
