import type { AppStateStatus } from "react-native";
import type {
  AppUpdateCheckResult,
  AppUpdateRunGuard,
} from "./app-update-info";

export type AutomaticAppUpdateOptions = {
  enabled: boolean;
  beforeCheck?: () => Promise<{
    allowed: boolean;
    epoch: number;
  }>;
  getEpoch?: () => number;
};

export type AutomaticAppUpdateDependencies = {
  checkAndDownload: (
    guard: AutomaticAppUpdateRunGuard,
  ) => Promise<AppUpdateCheckResult>;
  promptRestart: (guard: {
    expectedEpoch: number;
    beforeApply: () => Promise<boolean>;
    isCurrent: () => boolean;
  }) => void;
  warn: (error: unknown) => void;
};

export type AutomaticAppUpdateRunGuard = AppUpdateRunGuard & {
  signal: AbortSignal;
};

type AutomaticAppUpdateInFlight = {
  generation: number;
  controller: AbortController;
  promise: Promise<void>;
};

export type AutomaticAppUpdateController = {
  updateOptions: (options: AutomaticAppUpdateOptions) => void;
  cancel: () => void;
  check: (options?: AutomaticAppUpdateOptions) => Promise<void>;
  handleAppStateChange: (
    previousState: AppStateStatus,
    nextState: AppStateStatus,
    options?: AutomaticAppUpdateOptions
  ) => Promise<void>;
};

export function createAutomaticAppUpdateApplyHandler(input: {
  beforeApply: () => Promise<boolean>;
  isCurrent?: () => boolean;
  apply: () => Promise<void>;
  warn: (error: unknown) => void;
}) {
  let inFlight: Promise<void> | null = null;
  const isCurrent = input.isCurrent ?? (() => true);

  return () => {
    if (inFlight) {
      return inFlight;
    }

    inFlight = (async () => {
      try {
        if (!isCurrent()) {
          return;
        }
        const allowed = await input.beforeApply();
        if (!allowed || !isCurrent()) {
          return;
        }
        await input.apply();
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
  let liveOptions: AutomaticAppUpdateOptions = { enabled: false };
  let runGeneration = 0;
  let inFlight: AutomaticAppUpdateInFlight | null = null;
  let downloaded = false;

  function updateOptions(options: AutomaticAppUpdateOptions) {
    liveOptions = options;
  }

  function cancel() {
    runGeneration += 1;
    inFlight?.controller.abort();
    downloaded = false;
  }

  function isGenerationCurrent(
    generation: number,
    controller: AbortController,
  ) {
    return (
      liveOptions.enabled
      && runGeneration === generation
      && !controller.signal.aborted
    );
  }

  function isEpochCurrent(expectedEpoch: number) {
    return (
      liveOptions.getEpoch === undefined
      || liveOptions.getEpoch() === expectedEpoch
    );
  }

  async function check(
    options?: AutomaticAppUpdateOptions,
  ): Promise<void> {
    if (options) {
      updateOptions(options);
    }
    if (!liveOptions.enabled || downloaded) {
      return;
    }
    if (inFlight) {
      if (inFlight.generation === runGeneration) {
        return inFlight.promise;
      }
      // 已取消的 Expo Promise 无法物理中止；等它退出后再启动新代，避免原生 API 并发。
      return inFlight.promise.then(() => check());
    }

    const generation = runGeneration + 1;
    runGeneration = generation;
    const controller = new AbortController();
    let expectedEpoch: number | null = null;
    const runGuard: AutomaticAppUpdateRunGuard = {
      signal: controller.signal,
      isCurrent: () => (
        isGenerationCurrent(generation, controller)
        && (
          expectedEpoch === null
          || isEpochCurrent(expectedEpoch)
        )
      ),
    };
    let task!: AutomaticAppUpdateInFlight;
    const promise = (async () => {
      try {
        const barrierOptions = liveOptions;
        let barrier = {
          allowed: true,
          epoch: barrierOptions.getEpoch?.() ?? 0,
        };
        if (barrierOptions.beforeCheck) {
          barrier = await barrierOptions.beforeCheck();
        }
        if (!isGenerationCurrent(generation, controller)) {
          return;
        }

        expectedEpoch = barrier.epoch;
        if (
          !barrier.allowed
          || downloaded
          || !isEpochCurrent(expectedEpoch)
        ) {
          return;
        }

        const result = await dependencies.checkAndDownload(runGuard);
        if (
          result.status !== "downloaded"
          || downloaded
          || !runGuard.isCurrent()
        ) {
          return;
        }

        // 更新包只提示一次；取消会使本 generation 和旧 Alert 的点击守卫同时失效。
        downloaded = true;
        dependencies.promptRestart({
          expectedEpoch,
          isCurrent: runGuard.isCurrent,
          beforeApply: async () => {
            try {
              if (!isGenerationCurrent(generation, controller)) {
                return false;
              }

              const applyOptions = liveOptions;
              if (!applyOptions.beforeCheck) {
                return (
                  expectedEpoch !== null
                  && isEpochCurrent(expectedEpoch)
                );
              }

              // Alert 可停留很久；点击时读取 live options 并重新建立 fresh epoch。
              const freshBarrier = await applyOptions.beforeCheck();
              if (!isGenerationCurrent(generation, controller)) {
                return false;
              }
              expectedEpoch = freshBarrier.epoch;
              return (
                freshBarrier.allowed
                && isEpochCurrent(expectedEpoch)
              );
            } catch (error) {
              if (isGenerationCurrent(generation, controller)) {
                dependencies.warn(error);
              }
              return false;
            }
          },
        });
      } catch (error) {
        // 被 cancel 的旧代静默退出；真实检查失败仍记录供日志观察。
        if (isGenerationCurrent(generation, controller)) {
          dependencies.warn(error);
        }
      }
    })().finally(() => {
      if (inFlight === task) {
        inFlight = null;
      }
    });

    task = { generation, controller, promise };
    inFlight = task;
    return promise;
  }

  async function handleAppStateChange(
    previousState: AppStateStatus,
    nextState: AppStateStatus,
    options?: AutomaticAppUpdateOptions
  ) {
    if (options) {
      updateOptions(options);
    }
    if (!shouldCheckOnAppStateChange(previousState, nextState)) {
      return;
    }

    await check();
  }

  return {
    updateOptions,
    cancel,
    check,
    handleAppStateChange,
  };
}
