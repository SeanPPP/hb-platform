import { beforeEach, expect, jest, test } from "@jest/globals";
import { render, waitFor } from "@testing-library/react-native";

import RemoteHistoryRoute from "../../../app/remote-history";

let mockRuntime: any;
let mockActiveCashier: any;
let mockScreenProps: any;
const mockClearActiveCashier = jest.fn();
const mockCreatePresenter = jest.fn();
const mockDestroyPresenter = jest.fn();
const mockRouterDismissTo = jest.fn();
const mockRouterPush = jest.fn();

jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    Redirect: ({ href }: { href: string }) =>
      React.createElement(Text, { testID: "redirect" }, href),
    useRouter: () => ({
      dismissTo: mockRouterDismissTo,
      push: mockRouterPush,
    }),
  };
});

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

jest.mock("@/features/cashier-login", () => ({
  isActiveCashierBoundToDevice: (
    cashier: Readonly<{ storeCode: string; deviceCode: string }>,
    identity: Readonly<{ storeCode: string; deviceCode: string }>,
  ) =>
    cashier.storeCode === identity.storeCode &&
    cashier.deviceCode === identity.deviceCode,
  resolveProtectedSalesRouteGate: (
    runtime: Readonly<{ phase: string; device: string }>,
    cashier: unknown,
  ) => {
    if (
      !["ready", "ready-offline"].includes(runtime.phase) ||
      !["authorized-local", "authorized-online"].includes(runtime.device)
    ) {
      return "redirect-index";
    }
    return cashier ? "check-device-identity" : "redirect-login";
  },
  useCashierLoginStore: (selector: (state: unknown) => unknown) =>
    selector({
      activeCashier: mockActiveCashier,
      clearActiveCashier: mockClearActiveCashier,
    }),
}));

jest.mock("@/features/remote-history", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    REMOTE_HISTORY_VIEW_PERMISSION:
      "Permissions.PosTerminal.History.View",
    hasRemoteHistoryViewPermission: (permissions: readonly string[]) =>
      permissions.includes("Permissions.PosTerminal.History.View"),
    resolveRemoteHistoryPresenterFactory: (services: any) =>
      services?.remoteHistory ?? null,
    RemoteHistoryScreen: (props: unknown) => {
      mockScreenProps = props;
      return React.createElement(
        Text,
        { testID: "remote-history-screen" },
        "remote history",
      );
    },
    RemoteHistoryUnavailableScreen: ({ onBack }: { onBack(): void }) =>
      React.createElement(
        Text,
        { onPress: onBack, testID: "remote-history-unavailable" },
        "unavailable",
      ),
  };
});

jest.mock("@/ui/screens/bootstrap-screen", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    BootstrapScreen: () =>
      React.createElement(Text, { testID: "bootstrap" }, "bootstrap"),
  };
});

beforeEach(() => {
  jest.clearAllMocks();
  mockScreenProps = null;
  mockActiveCashier = {
    cashierId: "C1",
    cashierName: "Cashier",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    permissions: [
      "Permissions.PosTerminal.History.View",
      "Permissions.PosTerminal.Returns.View",
    ],
    source: "online",
  };
  mockCreatePresenter.mockReturnValue({
    destroy: mockDestroyPresenter,
  });
  mockRuntime = readyRuntime();
});

test("复核设备后以可信身份创建 presenter，返回销售并在卸载时销毁", async () => {
  const screen = await render(<RemoteHistoryRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("remote-history-screen")).toBeTruthy();
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith({ online: true });

  mockScreenProps.onBack();
  expect(mockRouterDismissTo).toHaveBeenCalledWith("/sales");
  mockScreenProps.onRefund("10000000-0000-4000-8000-000000000001");
  expect(mockRouterPush).toHaveBeenCalledWith({
    pathname: "/returns",
    params: {
      orderRef: "10000000-0000-4000-8000-000000000001",
    },
  });
  await screen.unmount();
  expect(mockDestroyPresenter).toHaveBeenCalledTimes(1);
});

test("离线 runtime 仍进入只读 presenter，但明确传 online=false", async () => {
  mockRuntime = readyRuntime({
    phase: "ready-offline",
    backend: "offline",
    device: "authorized-local",
  });
  const screen = await render(<RemoteHistoryRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("remote-history-screen")).toBeTruthy();
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith(
    expect.objectContaining({ online: false }),
  );
});

test("缺 View 权限时返回销售且不创建 presenter", async () => {
  mockActiveCashier = { ...mockActiveCashier, permissions: [] };
  const screen = await render(<RemoteHistoryRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/sales");
  expect(mockCreatePresenter).not.toHaveBeenCalled();
});

test("缺 Returns.View 权限时不向远程历史暴露退款入口", async () => {
  mockActiveCashier = {
    ...mockActiveCashier,
    permissions: ["Permissions.PosTerminal.History.View"],
  };
  const screen = await render(<RemoteHistoryRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("remote-history-screen")).toBeTruthy();
  });
  expect(mockScreenProps.onRefund).toBeUndefined();
});

test("returns service 不可用时不向远程历史暴露退款入口", async () => {
  mockRuntime = readyRuntime({}, undefined, true, "unavailable");
  const screen = await render(<RemoteHistoryRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("remote-history-screen")).toBeTruthy();
  });
  expect(mockScreenProps.onRefund).toBeUndefined();
});

test("设备绑定不一致时清除收银会话", async () => {
  mockRuntime = readyRuntime(
    {},
    { storeCode: "S2", deviceCode: "IPAD-2" },
  );
  const screen = await render(<RemoteHistoryRoute />);

  await waitFor(() => {
    expect(mockClearActiveCashier).toHaveBeenCalledTimes(1);
  });
  expect(screen.getByTestId("bootstrap")).toBeTruthy();
});

test("runtime 尚未接线时显示受控不可用状态，不尝试网络", async () => {
  mockRuntime = readyRuntime({}, undefined, false);
  const screen = await render(<RemoteHistoryRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("remote-history-unavailable")).toBeTruthy();
  });
  expect(mockCreatePresenter).not.toHaveBeenCalled();
});

function readyRuntime(
  state: Partial<{
    phase: string;
    backend: string;
    device: string;
  }> = {},
  identity: Readonly<{ storeCode: string; deviceCode: string }> | undefined = {
    storeCode: "S1",
    deviceCode: "IPAD-1",
  },
  includeRemoteHistory = true,
  returnsStatus: "available" | "unavailable" = "available",
) {
  return {
    state: {
      phase: state.phase ?? "ready",
      backend: state.backend ?? "reachable",
      device: state.device ?? "authorized-online",
    },
    services: {
      deviceSession: {
        async getDeviceIdentity() {
          return identity;
        },
      },
      ...(includeRemoteHistory
        ? { remoteHistory: { createPresenter: mockCreatePresenter } }
        : {}),
      returns: { status: returnsStatus },
    },
  };
}
