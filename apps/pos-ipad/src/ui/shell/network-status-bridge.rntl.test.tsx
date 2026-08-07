import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import { act, cleanup, render, waitFor } from "@testing-library/react-native";
import {
  AppState,
  type AppStateEvent,
  type AppStateStatus,
} from "react-native";

import { usePosShellStore } from "./pos-shell-store";
import { NetworkStatusBridge } from "./network-status-bridge";

// 设备网络层：固定返回“设备在线”（模拟 Wi-Fi 正常，后端却已停止的场景）。
jest.mock("expo-network", () => ({
  getNetworkStateAsync: async () => ({
    isConnected: true,
    isInternetReachable: true,
  }),
  addNetworkStateListener: () => ({ remove: jest.fn() }),
}));

// app.config extra：本地 API 地址（与用户环境 192.168.31.246:5159 一致）。
jest.mock("expo-constants", () => ({
  __esModule: true,
  default: {
    expoConfig: {
      extra: {
        hbpos: {
          apiBaseUrl: "http://192.168.31.246:5159/pos-api",
          trustedApiOrigins: [],
        },
      },
    },
  },
}));

// 无持久化 API 配置：走 app.config extra 兜底。
jest.mock("expo-secure-store", () => ({
  getItemAsync: async () => null,
  setItemAsync: async () => undefined,
  deleteItemAsync: async () => undefined,
  WHEN_UNLOCKED: "WHEN_UNLOCKED",
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: "WHEN_UNLOCKED_THIS_DEVICE_ONLY",
}));

let appStateListener: ((state: AppStateStatus) => void) | null = null;

beforeEach(() => {
  usePosShellStore.getState().reset();
  appStateListener = null;
  jest.spyOn(AppState, "addEventListener").mockImplementation(
    (eventName: AppStateEvent, handler: (state: AppStateStatus) => void) => {
      if (eventName === "change") {
        appStateListener = handler;
      }
      return { remove: jest.fn() } as never;
    },
  );
});

afterEach(async () => {
  await cleanup();
  jest.restoreAllMocks();
});

test("设备在线但后端已停止（health 非 2xx）时，收银页 connectivity 翻转为 offline", async () => {
  // 后端停止：health 请求失败（非 2xx）。
  const fetchMock = jest.fn(async () => ({ ok: false })) as unknown as typeof fetch;
  (globalThis as { fetch: typeof fetch }).fetch = fetchMock;

  await act(async () => {
    await render(<NetworkStatusBridge />);
  });

  await waitFor(() => {
    expect(usePosShellStore.getState().connectivity).toBe("offline");
  });
  expect(fetchMock).toHaveBeenCalledWith(
    "http://192.168.31.246:5159/pos-api/api/v1/health",
    expect.objectContaining({ method: "GET" }),
  );
});

test("后端恢复（health 2xx）后，收银页 connectivity 翻转为 online", async () => {
  const fetchMock = jest.fn(async () => ({ ok: true })) as unknown as typeof fetch;
  (globalThis as { fetch: typeof fetch }).fetch = fetchMock;

  await act(async () => {
    await render(<NetworkStatusBridge />);
  });

  await waitFor(() => {
    expect(usePosShellStore.getState().connectivity).toBe("online");
  });
});

test("App 回到前台时立即重新探测后端", async () => {
  // 第一次探测失败（后端停止），前台恢复后探测成功（后端已恢复）。
  let reachable = false;
  const fetchMock = jest.fn(
    async () => ({ ok: reachable }),
  ) as unknown as typeof fetch;
  (globalThis as { fetch: typeof fetch }).fetch = fetchMock;

  await act(async () => {
    await render(<NetworkStatusBridge />);
  });
  await waitFor(() => {
    expect(usePosShellStore.getState().connectivity).toBe("offline");
  });

  // 后端恢复，App 回到前台触发重新探测。
  reachable = true;
  await act(async () => {
    appStateListener?.("active");
  });
  await waitFor(() => {
    expect(usePosShellStore.getState().connectivity).toBe("online");
  });
});
