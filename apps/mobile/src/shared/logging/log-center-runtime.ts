import { buildApiBaseUrl, getCurrentApiHost } from "@/shared/api/config";
import {
  createApplicationLogItem,
  type ApplicationLogInput,
  type ApplicationLogIngestItem,
  type LogCenterConfig,
  normalizeLogCenterConfig,
} from "@/shared/logging/log-center";

interface QueuedApplicationLog {
  item: ApplicationLogIngestItem;
  attempts: number;
}

type GlobalScopeWithHandlers = typeof globalThis & {
  ErrorUtils?: {
    getGlobalHandler?: () => ((error: unknown, isFatal?: boolean) => void) | undefined;
    setGlobalHandler?: (handler: (error: unknown, isFatal?: boolean) => void) => void;
  };
  onunhandledrejection?: ((event: unknown) => void) | null;
};

let pendingLogs: QueuedApplicationLog[] = [];
let flushTimer: ReturnType<typeof setTimeout> | null = null;
let flushInFlight = false;
let globalHandlerInstalled = false;
let configOverrideForTests: LogCenterConfig | null = null;
let userContextOverrideForTests: { userId?: string; userName?: string } | null = null;

function getExpoConfigExtra() {
  try {
    // 运行时按需读取 Expo 配置，避免测试环境提前加载 react-native 依赖。
    const constantsModule = require("expo-constants") as {
      default?: {
        expoConfig?: {
          extra?: Record<string, unknown>;
        };
      };
    };

    return constantsModule.default?.expoConfig?.extra;
  } catch {
    return undefined;
  }
}

function getLogCenterConfig() {
  if (configOverrideForTests) {
    return configOverrideForTests;
  }

  const fallbackApiBaseUrl = buildApiBaseUrl(getCurrentApiHost());
  const rawConfig = getExpoConfigExtra()?.logCenter;
  return normalizeLogCenterConfig(rawConfig, fallbackApiBaseUrl);
}

function getUserLogContext() {
  if (userContextOverrideForTests) {
    return userContextOverrideForTests;
  }

  try {
    // 这里同样采用惰性读取，避免日志测试被鉴权 store 的原生依赖拖入。
    const authStoreModule = require("@/store/auth-store") as {
      useAuthStore?: {
        getState?: () => {
          user?: {
            userGuid?: string;
            userGUID?: string;
            username?: string;
            fullName?: string;
          } | null;
        };
      };
    };
    const user = authStoreModule.useAuthStore?.getState?.().user;
    return {
      userId: user?.userGuid || user?.userGUID || undefined,
      userName: user?.username || user?.fullName || undefined,
    };
  } catch {
    return {};
  }

}

function trimPendingLogs(maxQueueSize: number) {
  if (pendingLogs.length <= maxQueueSize) {
    return;
  }

  pendingLogs = pendingLogs.slice(pendingLogs.length - maxQueueSize);
}

function scheduleFlush(delayMs = 0) {
  if (flushTimer) {
    return;
  }

  flushTimer = setTimeout(() => {
    flushTimer = null;
    void flushPendingLogs();
  }, delayMs);
}

function requeueLogs(batch: QueuedApplicationLog[], config: LogCenterConfig) {
  const retryable = batch
    .filter((entry) => entry.attempts < config.retryLimit)
    .map((entry) => ({
      ...entry,
      attempts: entry.attempts + 1,
    }));

  if (!retryable.length) {
    return;
  }

  pendingLogs = [...retryable, ...pendingLogs];
  trimPendingLogs(config.maxQueueSize);
  scheduleFlush(1500);
}

async function flushPendingLogs() {
  if (flushInFlight || !pendingLogs.length) {
    return;
  }

  const config = getLogCenterConfig();
  if (!config.enabled) {
    pendingLogs = [];
    return;
  }

  flushInFlight = true;
  const batch = pendingLogs.splice(0, config.batchSize);

  try {
    const response = await fetch(config.endpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Log-Project": config.projectCode,
        "X-Log-Key": config.key,
        "X-Skip-Center-Log": "1",
      },
      body: JSON.stringify({
        logs: batch.map((entry) => entry.item),
      }),
    });

    if (!response.ok) {
      requeueLogs(batch, config);
    }
  } catch {
    requeueLogs(batch, config);
  } finally {
    flushInFlight = false;
    if (pendingLogs.length) {
      scheduleFlush();
    }
  }
}

function clearFlushTimer() {
  if (!flushTimer) {
    return;
  }

  clearTimeout(flushTimer);
  flushTimer = null;
}

export function reportApplicationLog(input: ApplicationLogInput) {
  try {
    const config = getLogCenterConfig();
    if (!config.enabled) {
      return;
    }

    pendingLogs.push({
      item: createApplicationLogItem(
        {
          ...getUserLogContext(),
          ...input,
        },
        config
      ),
      attempts: 0,
    });
    trimPendingLogs(config.maxQueueSize);
    scheduleFlush();
  } catch {
    // 日志上报必须吞掉所有异常，不能反向影响业务链路。
  }
}

export function __setLogCenterConfigForTests(config: LogCenterConfig | null) {
  configOverrideForTests = config;
}

export function __setUserLogContextForTests(context: { userId?: string; userName?: string } | null) {
  userContextOverrideForTests = context;
}

export function __getPendingLogCountForTests() {
  return pendingLogs.length;
}

export async function __flushPendingLogsForTests() {
  await flushPendingLogs();
}

export function __resetLogCenterRuntimeForTests() {
  clearFlushTimer();
  pendingLogs = [];
  flushInFlight = false;
  configOverrideForTests = null;
  userContextOverrideForTests = null;
}

function normalizeUnhandledReason(reason: unknown) {
  if (reason instanceof Error) {
    return reason;
  }

  if (typeof reason === "string") {
    return new Error(reason);
  }

  return new Error("Unhandled promise rejection");
}

export function installGlobalErrorLogging() {
  if (globalHandlerInstalled) {
    return () => undefined;
  }

  globalHandlerInstalled = true;
  const disposers: Array<() => void> = [];
  const globalScope = globalThis as GlobalScopeWithHandlers;
  const previousErrorHandler = globalScope.ErrorUtils?.getGlobalHandler?.();

  if (globalScope.ErrorUtils?.setGlobalHandler) {
    globalScope.ErrorUtils.setGlobalHandler((error, isFatal) => {
      const normalizedError = error instanceof Error ? error : new Error(String(error));
      reportApplicationLog({
        level: isFatal ? "Critical" : "Error",
        message: isFatal ? "移动端发生未捕获致命异常" : "移动端发生未捕获异常",
        sourceType: "app.exception",
        exceptionType: normalizedError.name,
        exceptionMessage: normalizedError.message,
        stackTrace: normalizedError.stack,
        properties: {
          isFatal: Boolean(isFatal),
        },
      });
      previousErrorHandler?.(error, isFatal);
    });

    disposers.push(() => {
      if (previousErrorHandler && globalScope.ErrorUtils?.setGlobalHandler) {
        globalScope.ErrorUtils.setGlobalHandler(previousErrorHandler);
      }
    });
  }

  if (typeof globalScope.addEventListener === "function") {
    const rejectionHandler = (event: Event & { reason?: unknown }) => {
      const normalizedError = normalizeUnhandledReason(event.reason);
      reportApplicationLog({
        level: "Error",
        message: "移动端发生未处理 Promise 拒绝",
        sourceType: "app.promise",
        exceptionType: normalizedError.name,
        exceptionMessage: normalizedError.message,
        stackTrace: normalizedError.stack,
      });
    };

    globalScope.addEventListener("unhandledrejection", rejectionHandler as EventListener);
    disposers.push(() => {
      globalScope.removeEventListener?.("unhandledrejection", rejectionHandler as EventListener);
    });
  } else if ("onunhandledrejection" in globalScope) {
    const previousUnhandledRejection = globalScope.onunhandledrejection;
    globalScope.onunhandledrejection = (event) => {
      const reason = (event as { reason?: unknown } | undefined)?.reason;
      const normalizedError = normalizeUnhandledReason(reason);
      reportApplicationLog({
        level: "Error",
        message: "移动端发生未处理 Promise 拒绝",
        sourceType: "app.promise",
        exceptionType: normalizedError.name,
        exceptionMessage: normalizedError.message,
        stackTrace: normalizedError.stack,
      });
      previousUnhandledRejection?.(event);
    };

    disposers.push(() => {
      globalScope.onunhandledrejection = previousUnhandledRejection ?? null;
    });
  }

  return () => {
    for (const dispose of disposers.reverse()) {
      try {
        dispose();
      } catch {
        // 清理失败也不影响应用关闭或热更新。
      }
    }
    globalHandlerInstalled = false;
  };
}
