import { afterEach, expect, jest, test } from "@jest/globals";
import {
  act,
  cleanup,
  render,
  waitFor,
} from "@testing-library/react-native";
import { AppState } from "react-native";

import { usePosShellStore } from "./pos-shell-store";
import { RuntimeStatusBridge } from "./runtime-status-bridge";

type RuntimePhase =
  | "idle"
  | "starting"
  | "ready"
  | "ready-offline"
  | "registration-required"
  | "pending-approval"
  | "locked"
  | "failed";

let mockRuntime: any;

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

function runtimeState(phase: RuntimePhase) {
  return {
    phase,
    database: phase === "ready" || phase === "ready-offline" ? "ready" : "closed",
    backend: phase === "ready-offline" ? "offline" : "reachable",
    device:
      phase === "ready" || phase === "ready-offline"
        ? "authorized-online"
        : "unknown",
  };
}

afterEach(() => {
  cleanup();
  mockRuntime = null;
  usePosShellStore.getState().reset();
  jest.restoreAllMocks();
});

test("仅 ready 状态读取公开终端身份，其他状态和读取失败均清空", async () => {
  const getDevicePresentation = jest
    .fn<() => Promise<any>>()
    .mockResolvedValueOnce({
      storeName: "Brisbane CBD",
      storeCode: "001",
      deviceCode: "IPAD-07",
    })
    .mockResolvedValueOnce({
      storeName: "Brisbane CBD",
      storeCode: "001",
      deviceCode: "IPAD-07",
    })
    .mockRejectedValueOnce(new Error("secure store unavailable"));
  const services = { deviceSession: { getDevicePresentation } };
  mockRuntime = { state: runtimeState("ready"), services };

  const screen = await render(<RuntimeStatusBridge />);

  await waitFor(() => {
    expect(usePosShellStore.getState().terminalPresentation).toEqual({
      storeName: "Brisbane CBD",
      deviceCode: "IPAD-07",
    });
  });
  expect(getDevicePresentation).toHaveBeenCalledTimes(1);

  mockRuntime = { state: runtimeState("locked"), services };
  await screen.rerender(<RuntimeStatusBridge />);
  expect(usePosShellStore.getState().terminalPresentation).toBeNull();
  expect(getDevicePresentation).toHaveBeenCalledTimes(1);

  mockRuntime = { state: runtimeState("ready-offline"), services };
  await screen.rerender(<RuntimeStatusBridge />);
  await waitFor(() => {
    expect(getDevicePresentation).toHaveBeenCalledTimes(2);
    expect(usePosShellStore.getState().terminalPresentation).toEqual({
      storeName: "Brisbane CBD",
      deviceCode: "IPAD-07",
    });
  });

  mockRuntime = { state: runtimeState("ready"), services };
  await screen.rerender(<RuntimeStatusBridge />);
  await waitFor(() => {
    expect(getDevicePresentation).toHaveBeenCalledTimes(3);
    expect(usePosShellStore.getState().terminalPresentation).toBeNull();
  });
});

test("旧的异步读取在运行态变化后不得回写终端身份", async () => {
  let resolvePresentation:
    | ((value: {
        storeName: string;
        storeCode: string;
        deviceCode: string;
      }) => void)
    | undefined;
  const pending = new Promise<{
    storeName: string;
    storeCode: string;
    deviceCode: string;
  }>((resolve) => {
    resolvePresentation = resolve;
  });
  const getDevicePresentation = jest.fn(() => pending);
  const services = { deviceSession: { getDevicePresentation } };
  mockRuntime = { state: runtimeState("ready"), services };

  const screen = await render(<RuntimeStatusBridge />);
  expect(getDevicePresentation).toHaveBeenCalledTimes(1);

  mockRuntime = { state: runtimeState("locked"), services };
  await screen.rerender(<RuntimeStatusBridge />);
  expect(usePosShellStore.getState().terminalPresentation).toBeNull();

  await act(async () => {
    resolvePresentation?.({
      storeName: "Stale branch",
      storeCode: "OLD",
      deviceCode: "OLD-DEVICE",
    });
    await pending;
  });

  expect(usePosShellStore.getState().terminalPresentation).toBeNull();
});

test("待同步订单数在启动、drain 完成、前台恢复、连接恢复与服务身份变化后刷新", async () => {
  let foregroundListener: ((state: string) => void) | undefined;
  const removeForegroundListener = jest.fn();
  jest.spyOn(AppState, "addEventListener").mockImplementation(
    ((_event: string, listener: (state: string) => void) => {
      foregroundListener = listener;
      return { remove: removeForegroundListener };
    }) as typeof AppState.addEventListener,
  );
  const drainListeners = new Set<(event: any) => void>();
  let count = 4;
  let deferredCountRead: Promise<number> | null = null;
  const readPendingOrderSyncCount = jest.fn(
    () => deferredCountRead ?? Promise.resolve(count),
  );
  const unsubscribeDrainSettled = jest.fn();
  const sync = {
    readPendingOrderSyncCount,
    subscribeDrainSettled(listener: (event: any) => void) {
      drainListeners.add(listener);
      return () => {
        drainListeners.delete(listener);
        unsubscribeDrainSettled();
      };
    },
  };
  const services = {
    deviceSession: { getDevicePresentation: async () => null },
    sync,
  };
  mockRuntime = { state: runtimeState("ready"), services };

  const screen = await render(<RuntimeStatusBridge />);
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 4,
    });
  });

  count = 3;
  await act(async () => {
    for (const listener of [...drainListeners]) {
      listener({
        outcome: "fulfilled",
        report: {
          leased: 1,
          orderSucceeded: 1,
          orderRetried: 0,
          orderBlocked: 0,
          orderRejected: 0,
          auditUploaded: 0,
        },
      });
    }
  });
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 3,
    });
  });

  count = 2;
  await act(async () => {
    foregroundListener?.("active");
  });
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 2,
    });
  });

  const readsBeforeOffline = readPendingOrderSyncCount.mock.calls.length;
  await act(async () => {
    usePosShellStore.getState().setConnectivity("offline");
  });
  expect(readPendingOrderSyncCount).toHaveBeenCalledTimes(readsBeforeOffline);
  expect(usePosShellStore.getState().pendingSync).toEqual({
    kind: "ready",
    count: 2,
  });

  count = 1;
  await act(async () => {
    usePosShellStore.getState().setConnectivity("online");
  });
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 1,
    });
  });

  let resolveBackgroundRead: ((value: number) => void) | undefined;
  deferredCountRead = new Promise<number>((resolve) => {
    resolveBackgroundRead = resolve;
  });
  await act(async () => {
    for (const listener of [...drainListeners]) {
      listener({ outcome: "rejected" });
    }
    await Promise.resolve();
  });
  expect(usePosShellStore.getState().pendingSync).toEqual({
    kind: "ready",
    count: 1,
  });
  await act(async () => {
    resolveBackgroundRead?.(0);
    await deferredCountRead;
  });
  deferredCountRead = null;
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 0,
    });
  });

  const nextRead = jest.fn(async () => 0);
  mockRuntime = {
    state: runtimeState("ready"),
    services: {
      ...services,
      sync: {
        readPendingOrderSyncCount: nextRead,
        subscribeDrainSettled: () => () => undefined,
      },
    },
  };
  await screen.rerender(<RuntimeStatusBridge />);
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 0,
    });
  });
  expect(nextRead).toHaveBeenCalledTimes(1);
  expect(unsubscribeDrainSettled).toHaveBeenCalled();
});

test("待同步订单数读取失败显示不可用，运行时未就绪时保持检查中", async () => {
  mockRuntime = {
    state: runtimeState("ready"),
    services: {
      deviceSession: { getDevicePresentation: async () => null },
      sync: {
        readPendingOrderSyncCount: async () => {
          throw new Error("database unavailable");
        },
        subscribeDrainSettled: () => () => undefined,
      },
    },
  };
  const screen = await render(<RuntimeStatusBridge />);
  await waitFor(() => {
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "unavailable",
    });
  });

  mockRuntime = { state: runtimeState("starting"), services: null };
  await screen.rerender(<RuntimeStatusBridge />);
  expect(usePosShellStore.getState().pendingSync).toEqual({
    kind: "checking",
  });
});
