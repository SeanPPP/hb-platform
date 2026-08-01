import { expect, jest, test } from "@jest/globals";
import type { ErrorInfo } from "react";

import {
  ApplicationLogErrorBoundary,
  installApplicationErrorObserver,
} from "./application-error-observer";

function reporter() {
  return { record: jest.fn() };
}

function restoreGlobalProperty(
  name: string,
  descriptor: PropertyDescriptor | undefined,
): void {
  if (descriptor) {
    Object.defineProperty(globalThis, name, descriptor);
  } else {
    Reflect.deleteProperty(globalThis, name);
  }
}

type PromiseRejectionTrackerOptions = {
  allRejections?: boolean;
  onUnhandled?: (id: number, reason: unknown) => void;
  onHandled?: (id: number, reason: unknown) => void;
};

const reactNativePromiseRejectionTrackingOptions = jest.requireActual<{
  default: PromiseRejectionTrackerOptions;
}>("react-native/Libraries/promiseRejectionTrackingOptions").default;

test("React 根 ErrorBoundary 记录后重新抛出，不吞掉原始异常", () => {
  const log = reporter();
  const boundary = new ApplicationLogErrorBoundary({
    applicationLog: log as never,
    children: null,
  });
  const error = new Error("render failed");

  expect(() =>
    boundary.componentDidCatch(error, {
      componentStack: "at CheckoutScreen",
    } as ErrorInfo),
  ).toThrow(error);
  expect(log.record).toHaveBeenCalledWith(expect.objectContaining({
    category: "runtime.react-error-boundary",
    error,
  }));
});

test("ErrorUtils 观察器记录异常后仍调用旧 handler，记录失败不递归", () => {
  const previous = jest.fn();
  let installed: ((error: unknown, isFatal?: boolean) => void) | undefined;
  const originalErrorUtils = (globalThis as { ErrorUtils?: unknown }).ErrorUtils;
  Object.assign(globalThis, {
    ErrorUtils: {
      getGlobalHandler: () => previous,
      setGlobalHandler: (handler: (error: unknown, isFatal?: boolean) => void) => {
        installed = handler;
      },
    },
  });
  const log = { record: jest.fn(() => { throw new Error("logger failed"); }) };

  try {
    const dispose = installApplicationErrorObserver(() => log as never);
    const error = new Error("fatal failure");
    installed?.(error, true);

    expect(log.record).toHaveBeenCalledTimes(1);
    expect(previous).toHaveBeenCalledWith(error, true);
    dispose();
  } finally {
    Object.assign(globalThis, { ErrorUtils: originalErrorUtils });
  }
});

test("Promise 观察器不 preventDefault，并在支持时卸载 event listener", () => {
  const originalAdd = globalThis.addEventListener;
  const originalRemove = globalThis.removeEventListener;
  const originalUnhandled = (globalThis as { onunhandledrejection?: unknown })
    .onunhandledrejection;
  const previous = jest.fn();
  let listener: ((event: { reason?: unknown }) => void) | undefined;
  const removed = jest.fn();
  Object.assign(globalThis, {
    addEventListener: (_type: string, handler: (event: { reason?: unknown }) => void) => {
      listener = handler;
    },
    removeEventListener: removed,
    onunhandledrejection: previous,
  });
  const log = reporter();

  try {
    const dispose = installApplicationErrorObserver(() => log as never);
    const error = new Error("promise failure");
    listener?.({ reason: error });

    expect(log.record).toHaveBeenCalledWith(expect.objectContaining({
      category: "runtime.unhandled-promise",
      error,
    }));
    expect(previous).not.toHaveBeenCalled();
    dispose();
    expect(removed).toHaveBeenCalledWith(
      "unhandledrejection",
      expect.any(Function),
    );
  } finally {
    Object.assign(globalThis, {
      addEventListener: originalAdd,
      removeEventListener: originalRemove,
      onunhandledrejection: originalUnhandled,
    });
  }
});

test("onunhandledrejection 后来被替换时，dispose 不覆盖新的宿主 handler", () => {
  const propertyNames = [
    "addEventListener",
    "removeEventListener",
    "onunhandledrejection",
  ] as const;
  const descriptors = new Map(
    propertyNames.map((name) => [
      name,
      Object.getOwnPropertyDescriptor(globalThis, name),
    ]),
  );
  const previous = jest.fn();
  const later = jest.fn();

  try {
    for (const name of propertyNames.slice(0, 2)) {
      Reflect.deleteProperty(globalThis, name);
    }
    Object.defineProperty(globalThis, "onunhandledrejection", {
      configurable: true,
      writable: true,
      value: previous,
    });

    const dispose = installApplicationErrorObserver(() => null);
    Object.assign(globalThis, { onunhandledrejection: later });

    dispose();

    expect(globalThis.onunhandledrejection).toBe(later);
  } finally {
    for (const name of propertyNames) {
      restoreGlobalProperty(name, descriptors.get(name));
    }
  }
});

test("RN Hermes release 无 DOM hook 时安装中心 tracker，并保留 console 诊断", () => {
  const propertyNames = [
    "addEventListener",
    "removeEventListener",
    "onunhandledrejection",
    "HermesInternal",
    "__DEV__",
  ] as const;
  const descriptors = new Map(
    propertyNames.map((name) => [
      name,
      Object.getOwnPropertyDescriptor(globalThis, name),
    ]),
  );
  let installedTracker: PromiseRejectionTrackerOptions = {};
  const enablePromiseRejectionTracker = jest.fn(
    (options: PromiseRejectionTrackerOptions) => {
      installedTracker = options;
    },
  );
  const log = reporter();
  const warn = jest.spyOn(console, "warn").mockImplementation(() => undefined);

  try {
    for (const name of propertyNames.slice(0, 3)) {
      Reflect.deleteProperty(globalThis, name);
    }
    Object.defineProperty(globalThis, "__DEV__", {
      configurable: true,
      writable: true,
      value: false,
    });
    Object.defineProperty(globalThis, "HermesInternal", {
      configurable: true,
      writable: true,
      value: {
        hasPromise: () => true,
        enablePromiseRejectionTracker,
      },
    });

    const dispose = installApplicationErrorObserver(() => log as never);
    const error = new Error("token=secret PAN=4111111111111111");
    error.stack =
      "Error: token=secret PAN=4111111111111111\n    at SECRET_STACK";
    installedTracker.onUnhandled?.(17, error);
    installedTracker.onUnhandled?.(17, error);
    installedTracker.onHandled?.(17, error);

    expect(enablePromiseRejectionTracker).toHaveBeenCalledTimes(1);
    expect(installedTracker.allRejections).toBe(true);
    expect(log.record).toHaveBeenCalledTimes(1);
    expect(log.record).toHaveBeenCalledWith(expect.objectContaining({
      category: "runtime.unhandled-promise",
      error,
    }));
    expect(warn).toHaveBeenCalled();

    dispose();
    installedTracker.onUnhandled?.(18, error);
    expect(log.record).toHaveBeenCalledTimes(1);
    expect(warn.mock.calls.flat()).not.toContain(error);
    expect(warn.mock.calls.every((parameters) => parameters.length === 1)).toBe(
      true,
    );
    const serializedConsoleParameters = JSON.stringify(
      warn.mock.calls,
      (_key, value: unknown) =>
        value instanceof Error
          ? { message: value.message, stack: value.stack }
          : value,
    );
    expect(serializedConsoleParameters).not.toMatch(
      /token=secret|4111111111111111|SECRET_STACK|"stack"/u,
    );
  } finally {
    warn.mockRestore();
    for (const name of propertyNames) {
      restoreGlobalProperty(name, descriptors.get(name));
    }
  }
});

test("RN Hermes dev 包装默认 tracker callbacks，不破坏 LogBox/console 路径", () => {
  const propertyNames = [
    "addEventListener",
    "removeEventListener",
    "onunhandledrejection",
    "HermesInternal",
    "__DEV__",
  ] as const;
  const descriptors = new Map(
    propertyNames.map((name) => [
      name,
      Object.getOwnPropertyDescriptor(globalThis, name),
    ]),
  );
  const defaults = reactNativePromiseRejectionTrackingOptions as PromiseRejectionTrackerOptions;
  const originalOnUnhandled = defaults.onUnhandled;
  const originalOnHandled = defaults.onHandled;
  const rnOnUnhandled = jest.fn();
  const rnOnHandled = jest.fn();
  let installedTracker: PromiseRejectionTrackerOptions = {};
  const enablePromiseRejectionTracker = jest.fn(
    (options: PromiseRejectionTrackerOptions) => {
      installedTracker = options;
    },
  );
  const log = reporter();

  try {
    defaults.onUnhandled = rnOnUnhandled;
    defaults.onHandled = rnOnHandled;
    for (const name of propertyNames.slice(0, 3)) {
      Reflect.deleteProperty(globalThis, name);
    }
    Object.defineProperty(globalThis, "__DEV__", {
      configurable: true,
      writable: true,
      value: true,
    });
    Object.defineProperty(globalThis, "HermesInternal", {
      configurable: true,
      writable: true,
      value: { enablePromiseRejectionTracker },
    });

    const dispose = installApplicationErrorObserver(() => log as never);
    const error = new Error("dev rejection");
    installedTracker.onUnhandled?.(21, error);
    installedTracker.onHandled?.(21, error);

    expect(enablePromiseRejectionTracker).toHaveBeenCalledTimes(1);
    expect(rnOnUnhandled).toHaveBeenCalledWith(21, error);
    expect(rnOnHandled).toHaveBeenCalledWith(21, error);
    expect(log.record).toHaveBeenCalledTimes(1);
    dispose();
  } finally {
    if (originalOnUnhandled) {
      defaults.onUnhandled = originalOnUnhandled;
    } else {
      delete defaults.onUnhandled;
    }
    if (originalOnHandled) {
      defaults.onHandled = originalOnHandled;
    } else {
      delete defaults.onHandled;
    }
    for (const name of propertyNames) {
      restoreGlobalProperty(name, descriptors.get(name));
    }
  }
});

test("Hermes tracker 只安装一次，重装更新 reader 且旧 dispose 不解绑新 reader", () => {
  const propertyNames = [
    "addEventListener",
    "removeEventListener",
    "onunhandledrejection",
    "HermesInternal",
    "__DEV__",
  ] as const;
  const descriptors = new Map(
    propertyNames.map((name) => [
      name,
      Object.getOwnPropertyDescriptor(globalThis, name),
    ]),
  );
  let installedTracker: PromiseRejectionTrackerOptions = {};
  const enablePromiseRejectionTracker = jest.fn(
    (options: PromiseRejectionTrackerOptions) => {
      installedTracker = options;
    },
  );
  const firstLog = reporter();
  const secondLog = reporter();
  const thirdLog = reporter();
  const warn = jest.spyOn(console, "warn").mockImplementation(() => undefined);

  try {
    for (const name of propertyNames.slice(0, 3)) {
      Reflect.deleteProperty(globalThis, name);
    }
    Object.defineProperty(globalThis, "__DEV__", {
      configurable: true,
      writable: true,
      value: false,
    });
    Object.defineProperty(globalThis, "HermesInternal", {
      configurable: true,
      writable: true,
      value: { enablePromiseRejectionTracker },
    });

    const disposeFirst = installApplicationErrorObserver(
      () => firstLog as never,
    );
    const disposeSecond = installApplicationErrorObserver(
      () => secondLog as never,
    );
    disposeFirst();
    installedTracker.onUnhandled?.(31, new Error("second reader"));

    expect(enablePromiseRejectionTracker).toHaveBeenCalledTimes(1);
    expect(firstLog.record).not.toHaveBeenCalled();
    expect(secondLog.record).toHaveBeenCalledTimes(1);

    disposeSecond();
    installedTracker.onUnhandled?.(32, new Error("no reader"));
    expect(secondLog.record).toHaveBeenCalledTimes(1);

    const disposeThird = installApplicationErrorObserver(
      () => thirdLog as never,
    );
    expect(enablePromiseRejectionTracker).toHaveBeenCalledTimes(1);
    installedTracker.onUnhandled?.(33, new Error("third reader"));
    expect(thirdLog.record).toHaveBeenCalledTimes(1);
    disposeThird();
  } finally {
    warn.mockRestore();
    for (const name of propertyNames) {
      restoreGlobalProperty(name, descriptors.get(name));
    }
  }
});
