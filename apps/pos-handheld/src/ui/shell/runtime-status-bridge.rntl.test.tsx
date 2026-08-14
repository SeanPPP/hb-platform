import { afterEach, expect, jest, test } from "@jest/globals";
import {
  act,
  cleanup,
  render,
  waitFor,
} from "@testing-library/react-native";

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
