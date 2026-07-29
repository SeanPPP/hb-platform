import { beforeEach, expect, jest, test } from "@jest/globals";
import { render, waitFor } from "@testing-library/react-native";

import HeldOrdersRoute from "../../../app/held-orders";

let mockRuntime: any;
let mockActiveCashier: any;
let mockScreenProps: any;
const mockClearActiveCashier = jest.fn();
const mockCreatePresenter = jest.fn();
const mockDestroyPresenter = jest.fn();
const mockRouterReplace = jest.fn();

jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    Redirect: ({ href }: { href: string }) =>
      React.createElement(Text, { testID: "redirect" }, href),
    useRouter: () => ({ replace: mockRouterReplace }),
  };
});

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

jest.mock("@/features/cashier-login", () => ({
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

jest.mock("@/features/held-orders", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    HeldOrdersScreen: (props: unknown) => {
      mockScreenProps = props;
      return React.createElement(
        Text,
        { testID: "held-orders-screen" },
        "held orders",
      );
    },
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
    userGuid: "user-1",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    permissions: [],
    source: "online",
  };
  mockCreatePresenter.mockReturnValue({
    destroy: mockDestroyPresenter,
  });
  mockRuntime = readyRuntime();
});

test("挂单路由零参数创建 presenter，返回销售且卸载时销毁", async () => {
  const screen = await render(<HeldOrdersRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("held-orders-screen")).toBeTruthy();
  });
  expect(mockCreatePresenter).toHaveBeenCalledTimes(1);
  expect(mockCreatePresenter).toHaveBeenCalledWith();

  mockScreenProps.onBack();
  expect(mockRouterReplace).toHaveBeenCalledWith("/sales");

  await screen.unmount();
  expect(mockDestroyPresenter).toHaveBeenCalledTimes(1);
});

test("presenter 创建失败时清除收银会话并安全返回登录", async () => {
  mockCreatePresenter.mockImplementationOnce(() => {
    throw new Error("current cashier is invalid");
  });
  const screen = await render(<HeldOrdersRoute />);

  await waitFor(() => {
    expect(mockClearActiveCashier).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("redirect").props.children).toBe("/login");
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith();
});

test("没有收银员会话时返回登录页", async () => {
  mockActiveCashier = null;
  const screen = await render(<HeldOrdersRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/login");
  expect(mockCreatePresenter).not.toHaveBeenCalled();
});

function readyRuntime() {
  return {
    state: { phase: "ready", device: "authorized-online" },
    services: {
      heldOrders: { createPresenter: mockCreatePresenter },
    },
  };
}
