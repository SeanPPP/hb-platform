import { beforeEach, expect, jest, test } from "@jest/globals";
import { render, waitFor } from "@testing-library/react-native";

import InstallmentsRoute from "../../../app/installments";

let mockRuntime: any;
let mockActiveCashier: any;
let mockScreenProps: any;
let mockUnavailableProps: any;
const mockClearActiveCashier = jest.fn();
const mockCreatePresenter = jest.fn();
const mockDestroyPresenter = jest.fn();
const mockSetOnline = jest.fn();
const mockSetCreateVoucherReference = jest.fn();
const mockSetRepaymentVoucherReference = jest.fn();
const mockGetDeviceIdentity = jest.fn<
  () => Promise<Readonly<{
    deviceCode: string;
    storeCode: string;
  }> | null>
>();
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

jest.mock("@/features/installments", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    resolveInstallmentsAccess: (permissions: readonly string[]) => ({
      canAddRepayment: permissions.includes(
        "Permissions.PosTerminal.Installments.AddRepayment",
      ),
      canCancel: permissions.includes(
        "Permissions.PosTerminal.Installments.Cancel",
      ),
      canConfirmPickup: permissions.includes(
        "Permissions.PosTerminal.Installments.ConfirmPickup",
      ),
      canCreate: permissions.includes(
        "Permissions.PosTerminal.Installments.Create",
      ),
      canView: permissions.includes(
        "Permissions.PosTerminal.Installments.View",
      ),
    }),
    resolveInstallmentsRuntimeFactory: (services: any) =>
      services.installments ?? null,
    InstallmentScreen: (props: unknown) => {
      mockScreenProps = props;
      return React.createElement(
        Text,
        { testID: "installments-screen" },
        "installments",
      );
    },
    InstallmentsUnavailableScreen: (props: unknown) => {
      mockUnavailableProps = props;
      return React.createElement(
        Text,
        { testID: "installments-runtime-unavailable" },
        "unavailable",
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
  mockUnavailableProps = null;
  mockActiveCashier = {
    cashierId: "C1",
    cashierName: "Cashier",
    deviceCode: "IPAD-1",
    permissions: ["Permissions.PosTerminal.Installments.View"],
    source: "online",
    storeCode: "S1",
  };
  mockGetDeviceIdentity.mockResolvedValue({
    deviceCode: "IPAD-1",
    storeCode: "S1",
  });
  mockCreatePresenter.mockReturnValue({
    destroy: mockDestroyPresenter,
    setOnline: mockSetOnline,
    setCreateVoucherReference: mockSetCreateVoucherReference,
    setRepaymentVoucherReference: mockSetRepaymentVoucherReference,
  });
  mockRuntime = readyRuntime();
});

test("复核设备身份后零参数创建 code-only presenter，路由不注入券 token", async () => {
  const screen = await render(<InstallmentsRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("installments-screen")).toBeTruthy();
  });
  expect(mockGetDeviceIdentity).toHaveBeenCalledTimes(1);
  expect(mockCreatePresenter).toHaveBeenCalledWith();
  expect(mockSetOnline).toHaveBeenCalledWith(true);
  expect(mockScreenProps.presenter).toBe(
    mockCreatePresenter.mock.results[0]?.value,
  );
  expect(
    "setCreateVoucherReservationToken" in mockScreenProps.presenter,
  ).toBe(false);
  expect(
    "setRepaymentVoucherReservationToken" in mockScreenProps.presenter,
  ).toBe(false);

  mockScreenProps.onBack();
  expect(mockRouterReplace).toHaveBeenCalledWith("/sales");
  await screen.unmount();
  expect(mockDestroyPresenter).toHaveBeenCalledTimes(1);
});

test("离线授权设备可浏览本地分期，但 presenter 收到离线状态", async () => {
  mockRuntime = readyRuntime({
    backend: "offline",
    device: "authorized-local",
    phase: "ready-offline",
  });
  const screen = await render(<InstallmentsRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("installments-screen")).toBeTruthy();
  });
  expect(mockSetOnline).toHaveBeenCalledWith(false);
  await screen.unmount();
});

test("缺少 View 权限的直链访问返回销售页且不读取身份", async () => {
  mockActiveCashier.permissions = [
    "Permissions.PosTerminal.Installments.Create",
  ];
  const screen = await render(<InstallmentsRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/sales");
  expect(mockGetDeviceIdentity).not.toHaveBeenCalled();
  expect(mockCreatePresenter).not.toHaveBeenCalled();
  await screen.unmount();
});

test("设备绑定不一致时清除活动收银员且不创建 presenter", async () => {
  mockGetDeviceIdentity.mockResolvedValue({
    deviceCode: "IPAD-2",
    storeCode: "S2",
  });
  const screen = await render(<InstallmentsRoute />);

  await waitFor(() => {
    expect(mockClearActiveCashier).toHaveBeenCalledTimes(1);
  });
  expect(screen.getByTestId("bootstrap")).toBeTruthy();
  expect(mockCreatePresenter).not.toHaveBeenCalled();
  await screen.unmount();
});

test("runtime 未接线时显示安全不可用页", async () => {
  delete mockRuntime.services.installments;
  const screen = await render(<InstallmentsRoute />);

  await waitFor(() => {
    expect(
      screen.getByTestId("installments-runtime-unavailable"),
    ).toBeTruthy();
  });
  expect(mockCreatePresenter).not.toHaveBeenCalled();
  mockUnavailableProps.onBack();
  expect(mockRouterReplace).toHaveBeenCalledWith("/sales");
  await screen.unmount();
});

test("没有活动收银员时返回登录页", async () => {
  mockActiveCashier = null;
  const screen = await render(<InstallmentsRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/login");
  await screen.unmount();
});

function readyRuntime(
  state: Partial<{
    backend: string;
    device: string;
    phase: string;
  }> = {},
) {
  return {
    services: {
      deviceSession: { getDeviceIdentity: mockGetDeviceIdentity },
      installments: { createPresenter: mockCreatePresenter },
    },
    state: {
      backend: "reachable",
      device: "authorized-online",
      phase: "ready",
      ...state,
    },
  };
}
