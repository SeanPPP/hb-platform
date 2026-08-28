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

test("同步数量在启动、drain 结束、离线恢复及前台恢复后只读刷新", async () => {
  const pendingCounts = [4, 3, 2, 1];
  const readPendingOrderSyncCount = jest.fn(async () => pendingCounts.shift() ?? 0);
  let drainSettled: (() => void) | null = null;
  let appStateChanged: ((state: string) => void) | null = null;
  const removeAppStateListener = jest.fn();
  jest.spyOn(AppState, "addEventListener").mockImplementation(
    ((_event: string, listener: (state: string) => void) => {
      appStateChanged = listener;
      return { remove: removeAppStateListener };
    }) as typeof AppState.addEventListener,
  );
  const unsubscribeDrain = jest.fn();
  const services = {
    deviceSession: { getDevicePresentation: jest.fn(async () => null) },
    sync: {
      readPendingOrderSyncCount,
      subscribeDrainSettled: (listener: () => void) => {
        drainSettled = listener;
        return unsubscribeDrain;
      },
    },
  };
  mockRuntime = { state: runtimeState("ready"), services };

  const screen = await render(<RuntimeStatusBridge />);
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 4,
    }),
  );

  await act(async () => drainSettled?.());
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 3,
    }),
  );

  await act(async () => {
    usePosShellStore.getState().setConnectivity("offline");
  });
  expect(readPendingOrderSyncCount).toHaveBeenCalledTimes(2);
  await act(async () => {
    usePosShellStore.getState().setConnectivity("online");
  });
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 2,
    }),
  );

  await act(async () => appStateChanged?.("active"));
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 1,
    }),
  );
  expect(readPendingOrderSyncCount).toHaveBeenCalledTimes(4);

  await act(async () => screen.unmount());
  expect(unsubscribeDrain).toHaveBeenCalledTimes(1);
  expect(removeAppStateListener).toHaveBeenCalledTimes(1);
});

test("生命周期后台刷新保留旧 ready 数量直到新读取完成", async () => {
  let drainSettled: (() => void) | null = null;
  let resolveRefresh: ((count: number) => void) | undefined;
  const readPendingOrderSyncCount = jest
    .fn<() => Promise<number>>()
    .mockResolvedValueOnce(5)
    .mockImplementationOnce(
      () =>
        new Promise<number>((resolve) => {
          resolveRefresh = resolve;
        }),
    );
  mockRuntime = {
    state: runtimeState("ready"),
    services: {
      deviceSession: { getDevicePresentation: jest.fn(async () => null) },
      sync: {
        readPendingOrderSyncCount,
        subscribeDrainSettled: (listener: () => void) => {
          drainSettled = listener;
          return () => undefined;
        },
      },
    },
  };
  await render(<RuntimeStatusBridge />);
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 5,
    }),
  );

  await act(async () => {
    drainSettled?.();
  });
  expect(usePosShellStore.getState().pendingSync).toEqual({
    kind: "ready",
    count: 5,
  });

  await act(async () => resolveRefresh?.(2));
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 2,
    }),
  );
});

test("同步数量读取失败显示不可用，服务身份变化后从检查中恢复", async () => {
  const firstReader = jest.fn(async () => {
    throw new Error("database closed");
  });
  const firstServices = {
    deviceSession: { getDevicePresentation: jest.fn(async () => null) },
    sync: {
      readPendingOrderSyncCount: firstReader,
      subscribeDrainSettled: () => () => undefined,
    },
  };
  mockRuntime = { state: runtimeState("ready"), services: firstServices };
  const screen = await render(<RuntimeStatusBridge />);

  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "unavailable",
    }),
  );

  let resolveCount: ((count: number) => void) | undefined;
  const secondReader = jest.fn(
    () =>
      new Promise<number>((resolve) => {
        resolveCount = resolve;
      }),
  );
  mockRuntime = {
    state: runtimeState("ready"),
    services: {
      ...firstServices,
      sync: {
        readPendingOrderSyncCount: secondReader,
        subscribeDrainSettled: () => () => undefined,
      },
    },
  };
  await screen.rerender(<RuntimeStatusBridge />);
  expect(usePosShellStore.getState().pendingSync).toEqual({
    kind: "checking",
  });

  await act(async () => resolveCount?.(7));
  await waitFor(() =>
    expect(usePosShellStore.getState().pendingSync).toEqual({
      kind: "ready",
      count: 7,
    }),
  );
  expect(firstReader).toHaveBeenCalledTimes(1);
  expect(secondReader).toHaveBeenCalledTimes(1);
});
