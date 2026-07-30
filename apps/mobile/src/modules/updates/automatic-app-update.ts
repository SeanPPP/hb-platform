import type { AppStateStatus } from "react-native";
import type { AppUpdateCheckResult } from "./app-update-info";

export type AutomaticAppUpdateOptions = {
  enabled: boolean;
  beforeCheck?: () => Promise<{
    allowed: boolean;
    epoch: number;
  }>;
  getEpoch?: () => number;
};

export type AutomaticAppUpdateDependencies = {
  checkAndDownload: () => Promise<AppUpdateCheckResult>;
  promptRestart: (guard: {
    expectedEpoch: number;
    beforeApply: () => Promise<boolean>;
  }) => void;
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

export function createAutomaticAppUpdateApplyHandler(input: {
  beforeApply: () => Promise<boolean>;
  apply: () => Promise<void>;
  warn: (error: unknown) => void;
}) {
  let inFlight: Promise<void> | null = null;

  return () => {
    if (inFlight) {
      return inFlight;
    }

    inFlight = (async () => {
      try {
        if (await input.beforeApply()) {
          await input.apply();
        }
      } catch (error) {
        input.warn(error);
      } finally {
        inFlight = null;
      }
    })();
    return inFlight;
  };
}

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
      let barrier = {
        allowed: true,
        epoch: options.getEpoch?.() ?? 0,
      };
      if (options.beforeCheck) {
        barrier = await options.beforeCheck();
      }
      if (
        !barrier.allowed ||
        downloaded ||
        !isBarrierCurrent(options, barrier.epoch)
      ) {
        return;
      }

      const result = await dependencies.checkAndDownload();
      if (
        result.status === "downloaded" &&
        isBarrierCurrent(options, barrier.epoch)
      ) {
        // 更新包只需要提示一次，避免用户回到前台时反复被打断。
        downloaded = true;
        const expectedEpoch = barrier.epoch;
        dependencies.promptRestart({
          expectedEpoch,
          beforeApply: async () => {
            try {
              if (!options.beforeCheck) {
                // 非 iOS 或未接原生版本协调器时保持原有直接应用行为。
                return isBarrierCurrent(options, expectedEpoch);
              }

              // Alert 可能停留很久；点击时必须重新获取原生决策，并紧接着核对 fresh epoch。
              const freshBarrier = await options.beforeCheck();
              return (
                freshBarrier.allowed &&
                isBarrierCurrent(options, freshBarrier.epoch)
              );
            } catch (error) {
              // 应用前校验失败按 fail-closed 处理，避免未处理 Promise 或绕过新 required。
              dependencies.warn(error);
              return false;
            }
          },
        });
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

function isBarrierCurrent(
  options: AutomaticAppUpdateOptions,
  expectedEpoch: number,
) {
  return (
    options.enabled &&
    (options.getEpoch === undefined ||
      options.getEpoch() === expectedEpoch)
  );
}
