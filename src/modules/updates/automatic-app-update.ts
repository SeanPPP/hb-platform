import type { AppStateStatus } from "react-native";
import type { AppUpdateCheckResult } from "./app-update-info";

export type AutomaticAppUpdateOptions = {
  enabled: boolean;
};

export type AutomaticAppUpdateDependencies = {
  checkAndDownload: () => Promise<AppUpdateCheckResult>;
  promptRestart: () => void;
  warn: (error: unknown) => void;
};

export type AutomaticAppUpdateController = {
  check: (options: AutomaticAppUpdateOptions) => Promise<void>;
  handleAppStateChange: (
    previousState: AppStateStatus,
    nextState: AppStateStatus,
    options: AutomaticAppUpdateOptions
  ) => Promise<void>;
};

function shouldCheckOnAppStateChange(previousState: AppStateStatus, nextState: AppStateStatus) {
  return previousState !== "active" && nextState === "active";
}

export function createAutomaticAppUpdateController(
  dependencies: AutomaticAppUpdateDependencies
): AutomaticAppUpdateController {
  let inFlight = false;
  let downloaded = false;

  async function check(options: AutomaticAppUpdateOptions) {
    if (!options.enabled || inFlight || downloaded) {
      return;
    }

    inFlight = true;
    try {
      const result = await dependencies.checkAndDownload();
      if (result.status === "downloaded") {
        // 更新包只需要提示一次，避免用户回到前台时反复被打断。
        downloaded = true;
        dependencies.promptRestart();
      }
    } catch (error) {
      // 自动检查失败不打断门店操作，只记录给调试和日志系统观察。
      dependencies.warn(error);
    } finally {
      inFlight = false;
    }
  }

  async function handleAppStateChange(
    previousState: AppStateStatus,
    nextState: AppStateStatus,
    options: AutomaticAppUpdateOptions
  ) {
    if (!shouldCheckOnAppStateChange(previousState, nextState)) {
      return;
    }

    await check(options);
  }

  return { check, handleAppStateChange };
}
