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
  const blockFetch = (): never => {
    // queryFn 内不能等待 cancelQueries 自己；先异步清理，再同步阻止网络请求。
    void clearCache();
    throw new SensitiveDetailFetchBlockedError();
  };

  return {
    async fetch<T>(fetcher: () => Promise<T>) {
      const startedGeneration = getActivityGeneration();
      if (!isActive()) {
        return blockFetch();
      }
      const result = await fetcher();
      if (!isActive() || startedGeneration !== getActivityGeneration()) {
        // 请求期间进入后台时丢弃迟到响应，避免重新写入 QueryCache。
        return blockFetch();
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
        if (!isActive() || startedGeneration !== getActivityGeneration()) {
          await clearCache();
          return false;
        }
        throw error;
      }
      if (!isActive() || startedGeneration !== getActivityGeneration()) {
        await clearCache();
        return false;
      }
      return true;
    },
    shouldIgnoreLateCallback(startedGeneration = getActivityGeneration()) {
      if (isActive() && startedGeneration === getActivityGeneration()) {
        return false;
      }
      // mutation 网络层未必能取消；迟到 callback 只负责清理，不再更新 UI 或重取详情。
      void clearCache();
      return true;
    },
  };
}
