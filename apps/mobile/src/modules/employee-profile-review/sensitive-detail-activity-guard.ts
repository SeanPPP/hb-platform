const SENSITIVE_DETAIL_FETCH_BLOCKED = "EMPLOYEE_PROFILE_SENSITIVE_DETAIL_FETCH_BLOCKED";

class SensitiveDetailFetchBlockedError extends Error {
  readonly code = SENSITIVE_DETAIL_FETCH_BLOCKED;

  constructor() {
    super("Sensitive detail fetch is blocked while the app is inactive");
    this.name = "SensitiveDetailFetchBlockedError";
  }
}

export function isSensitiveDetailFetchBlockedError(error: unknown) {
  return error instanceof SensitiveDetailFetchBlockedError
    || (
      typeof error === "object"
      && error !== null
      && "code" in error
      && error.code === SENSITIVE_DETAIL_FETCH_BLOCKED
    );
}

export function createEmployeeProfileSensitiveDetailActivityGuard({
  isActive,
  getActivityGeneration,
  clearCache,
}: {
  isActive: () => boolean;
  getActivityGeneration: () => number;
  clearCache: () => Promise<void>;
}) {
  const discardFetch = (): never => {
    throw new SensitiveDetailFetchBlockedError();
  };
  const clearAndDiscardFetch = (): never => {
    // queryFn 内不能等待 cancelQueries 自己；先异步清理，再同步阻止网络请求。
    void clearCache();
    return discardFetch();
  };

  return {
    async fetch<T>(fetcher: () => Promise<T>) {
      const startedGeneration = getActivityGeneration();
      if (!isActive()) {
        return clearAndDiscardFetch();
      }
      const result = await fetcher();
      if (!isActive()) {
        // 请求期间进入后台时丢弃迟到响应，避免重新写入 QueryCache。
        return clearAndDiscardFetch();
      }
      if (startedGeneration !== getActivityGeneration()) {
        // 当前已是新的 active 世代，只丢弃旧结果，绝不能清掉新世代共享缓存。
        return discardFetch();
      }
      return result;
    },
    async runIfActive(action: () => Promise<unknown>) {
      const startedGeneration = getActivityGeneration();
      if (!isActive()) {
        await clearCache();
        return false;
      }
      try {
        await action();
      } catch (error) {
        if (!isActive()) {
          await clearCache();
          return false;
        }
        if (startedGeneration !== getActivityGeneration()) {
          return false;
        }
        throw error;
      }
      if (!isActive()) {
        await clearCache();
        return false;
      }
      if (startedGeneration !== getActivityGeneration()) {
        return false;
      }
      return true;
    },
    shouldIgnoreLateCallback(startedGeneration = getActivityGeneration()) {
      if (!isActive()) {
        // mutation 网络层未必能取消；后台迟到 callback 只负责清理，不再更新 UI。
        void clearCache();
        return true;
      }
      // 新 active 世代已建立时只忽略旧 callback，保留重新鉴权写入的共享缓存。
      return startedGeneration !== getActivityGeneration();
    },
  };
}
