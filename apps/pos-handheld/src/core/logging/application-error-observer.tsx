import {
  Component,
  type ErrorInfo,
  type PropsWithChildren,
} from "react";

import type { ApplicationLogRuntime } from "./application-log";

type ApplicationLogReader = () => ApplicationLogRuntime | null;

type PromiseRejectionTrackerOptions = Readonly<{
  whitelist?: readonly unknown[] | null;
  allRejections?: boolean | null;
  onUnhandled?: ((id: number, reason: unknown) => void) | null;
  onHandled?: ((id: number, reason: unknown) => void) | null;
}>;

type PromiseRejectionTrackerSetter = (
  options: PromiseRejectionTrackerOptions | null,
) => void;

type GlobalScopeWithHandlers = typeof globalThis & {
  ErrorUtils?: {
    getGlobalHandler?: () =>
      | ((error: unknown, isFatal?: boolean) => void)
      | undefined;
    setGlobalHandler?: (
      handler: (error: unknown, isFatal?: boolean) => void,
    ) => void;
  };
  onunhandledrejection?: ((event: unknown) => void) | null;
  HermesInternal?: {
    enablePromiseRejectionTracker?: PromiseRejectionTrackerSetter;
  };
};

type ApplicationLogErrorBoundaryProps = PropsWithChildren<{
  applicationLog: ApplicationLogRuntime | null;
}>;

type ApplicationLogReaderBinding = Readonly<{
  owner: object;
  readApplicationLog: ApplicationLogReader;
}>;

let activeReaderBinding: ApplicationLogReaderBinding | null = null;
let globalObserverDisposers: (() => void)[] | null = null;
let installedHermesTrackerSetter: PromiseRejectionTrackerSetter | null = null;
const observedErrors = new WeakSet<object>();
const releasePromiseRejectionTrackingOptions: PromiseRejectionTrackerOptions = {
  allRejections: true,
  onUnhandled: (id) => {
    // release 只输出固定诊断与 id；reason 仅进入会脱敏的 ApplicationLogger。
    console.warn(`Possible unhandled promise rejection (id: ${id})`);
  },
  onHandled: (id) => {
    console.warn(`Promise rejection handled (id: ${id})`);
  },
};

/**
 * React ErrorBoundary 仅补充一次旁路记录，随后重抛给 RN 的原异常通道。
 * 这样不会展示替代 fallback，也不会悄悄改变原有崩溃或恢复语义。
 */
export class ApplicationLogErrorBoundary extends Component<ApplicationLogErrorBoundaryProps> {
  public componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    recordObservedError(this.props.applicationLog, error, {
      level: "Critical",
      message: "POS React render failed.",
      category: "runtime.react-error-boundary",
      properties: { componentStack: errorInfo.componentStack ?? "" },
    });
    throw error;
  }

  public render() {
    return this.props.children;
  }
}

/**
 * 复用 Expo/RN ErrorUtils 与可组合的 Promise hook；只观察、从不 preventDefault，
 * 并始终把未捕获错误交回安装前的 RN handler。
 */
export function installApplicationErrorObserver(
  readApplicationLog: ApplicationLogReader,
): () => void {
  const owner = {};
  activeReaderBinding = { owner, readApplicationLog };
  if (globalObserverDisposers) return createObserverDisposer(owner);

  const globalScope = globalThis as GlobalScopeWithHandlers;
  const disposers: (() => void)[] = [];
  const errorUtils = globalScope.ErrorUtils;
  const previousErrorHandler = errorUtils?.getGlobalHandler?.();
  if (
    previousErrorHandler &&
    errorUtils?.getGlobalHandler &&
    errorUtils.setGlobalHandler
  ) {
    const handler = (error: unknown, isFatal?: boolean) => {
      recordObservedError(readActiveApplicationLog(), error, {
        level: isFatal ? "Critical" : "Error",
        message: isFatal
          ? "POS encountered an uncaught fatal JavaScript error."
          : "POS encountered an uncaught JavaScript error.",
        category: "runtime.uncaught-javascript",
        properties: { isFatal: Boolean(isFatal) },
      });
      // 记录器失败不能影响 RN 原始异常链；原 handler 的抛出也必须保留。
      previousErrorHandler(error, isFatal);
    };
    errorUtils.setGlobalHandler(handler);
    disposers.push(() => {
      if (errorUtils.getGlobalHandler?.() === handler) {
        errorUtils.setGlobalHandler?.(previousErrorHandler);
      }
    });
  }

  const rejectionHandler = (event: { reason?: unknown }) => {
    const error = normalizeUnhandledReason(event.reason);
    recordObservedError(readActiveApplicationLog(), error, {
      level: "Error",
      message: "POS encountered an unhandled Promise rejection.",
      category: "runtime.unhandled-promise",
    });
    // 故意不调用 preventDefault：若宿主支持其默认异常展示，仍由宿主决定。
  };
  if (typeof globalScope.addEventListener === "function") {
    globalScope.addEventListener(
      "unhandledrejection",
      rejectionHandler as EventListener,
    );
    disposers.push(() => {
      globalScope.removeEventListener?.(
        "unhandledrejection",
        rejectionHandler as EventListener,
      );
    });
  } else if ("onunhandledrejection" in globalScope) {
    const previousUnhandledRejection = globalScope.onunhandledrejection;
    const handler = (event: unknown) => {
      rejectionHandler(event as { reason?: unknown });
      previousUnhandledRejection?.(event);
    };
    globalScope.onunhandledrejection = handler;
    disposers.push(() => {
      // 不覆盖 observer 生命周期内由宿主或其他库后来安装的 handler。
      if (globalScope.onunhandledrejection === handler) {
        globalScope.onunhandledrejection = previousUnhandledRejection ?? null;
      }
    });
  } else {
    installHermesPromiseRejectionTracker(globalScope);
  }

  globalObserverDisposers = disposers;
  return createObserverDisposer(owner);
}

function installHermesPromiseRejectionTracker(
  globalScope: GlobalScopeWithHandlers,
): void {
  const setter = globalScope.HermesInternal?.enablePromiseRejectionTracker;
  if (!setter || installedHermesTrackerSetter === setter) return;

  const defaults = readPromiseRejectionTrackingOptions();
  const defaultOnUnhandled = defaults.onUnhandled;
  const defaultOnHandled = defaults.onHandled;
  const tracker: PromiseRejectionTrackerOptions = {
    ...defaults,
    onUnhandled: (id, reason) => {
      recordObservedError(
        readActiveApplicationLog(),
        normalizeUnhandledReason(reason),
        {
          level: "Error",
          message: "POS encountered an unhandled Promise rejection.",
          category: "runtime.unhandled-promise",
        },
      );
      // dev 复用 RN LogBox；release 仅保留不含 reason 的固定 console 诊断。
      defaultOnUnhandled?.(id, reason);
    },
    onHandled: (id, reason) => {
      defaultOnHandled?.(id, reason);
    },
  };

  try {
    setter.call(globalScope.HermesInternal, tracker);
    // Hermes 无 getter/disable；同一 setter 只能由本模块覆盖一次。
    installedHermesTrackerSetter = setter;
  } catch {
    // tracker 安装失败不能阻断启动；下次 observer 安装仍可重试。
  }
}

function readPromiseRejectionTrackingOptions(): PromiseRejectionTrackerOptions {
  if (__DEV__) {
    // 与 RN polyfillPromise 相同的 dev-only 字面量 require，release 不引入 LogBox。
    // eslint-disable-next-line @typescript-eslint/no-require-imports -- RN 0.81.5 此 subpath 没有 d.ts。
    const module = require(
      "react-native/Libraries/promiseRejectionTrackingOptions"
    ) as { default: PromiseRejectionTrackerOptions };
    return module.default;
  }
  return releasePromiseRejectionTrackingOptions;
}

function createObserverDisposer(owner: object): () => void {
  return () => {
    if (activeReaderBinding?.owner !== owner) return;
    activeReaderBinding = null;

    const disposers = globalObserverDisposers;
    globalObserverDisposers = null;
    for (const dispose of [...(disposers ?? [])].reverse()) {
      try {
        dispose();
      } catch {
        // 卸载失败不能阻塞热更新、runtime 停止或 RN 默认恢复路径。
      }
    }
    // Hermes tracker 不可读取也不可恢复；其 callback 保留，仅解绑当前 reader。
  };
}

function readActiveApplicationLog(): ApplicationLogRuntime | null {
  return activeReaderBinding
    ? readSafely(activeReaderBinding.readApplicationLog)
    : null;
}

function recordObservedError(
  applicationLog: ApplicationLogRuntime | null,
  error: unknown,
  draft: Readonly<{
    level: "Error" | "Critical";
    message: string;
    category: string;
    properties?: Readonly<Record<string, unknown>>;
  }>,
): void {
  if (error && typeof error === "object") {
    if (observedErrors.has(error)) return;
    observedErrors.add(error);
  }
  try {
    applicationLog?.record({
      ...draft,
      error,
    });
  } catch {
    // 观察器不能二次进入 ErrorUtils，也不能阻断原始异常传播。
  }
}

function normalizeUnhandledReason(reason: unknown): Error {
  if (reason instanceof Error) return reason;
  return typeof reason === "string"
    ? new Error(reason)
    : new Error("Unhandled promise rejection");
}

function readSafely(
  readApplicationLog: ApplicationLogReader,
): ApplicationLogRuntime | null {
  try {
    return readApplicationLog();
  } catch {
    return null;
  }
}
