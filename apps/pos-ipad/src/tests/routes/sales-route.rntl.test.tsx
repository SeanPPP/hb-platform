import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import { act, render, waitFor } from "@testing-library/react-native";

import SalesRoute from "../../../app/sales";

let mockRuntime: any;
let mockActiveCashier: any;
let mockSalesScreenProps: any;
let mockCameraScannerProps: any;
let mockRouteCaptureProps: any;
let mockUpdateGate: any;
let mockUpdatePolicy: any;
const mockClearActiveCashier = jest.fn();
const mockCreatePresenter = jest.fn();
const mockDestroyPresenter = jest.fn();
const mockRouterPush = jest.fn();
const mockRouterReplace = jest.fn();
const mockHasRecoveryRequired = jest.fn<() => Promise<boolean>>();
const mockRandomUUID = jest.fn();
const mockAddLookupCode = jest.fn<() => Promise<boolean>>();
const mockSetQuery = jest.fn();
const mockReleaseCameraContext = jest.fn();
const mockAcquireScannerContext = jest.fn(
  (_context: string) => mockReleaseCameraContext,
);
const mockToggleAppLanguage = jest.fn<() => Promise<"en" | "zh">>();
const mockReadSalesToolbarOrder = jest.fn<() => string[] | null>();
const mockSaveSalesToolbarOrder =
  jest.fn<(order: readonly string[]) => Promise<void>>();
const mockReconcileSalesToolbarOrder =
  jest.fn<(order: readonly string[] | null | undefined) => string[]>();

const DEFAULT_TOOLBAR_ORDER = ["held-orders", "hold", "language", "lock"];

jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    Redirect: ({ href }: { href: string }) =>
      React.createElement(Text, { testID: "redirect" }, href),
    useRouter: () => ({ push: mockRouterPush, replace: mockRouterReplace }),
  };
});

jest.mock("expo-crypto", () => ({
  randomUUID: () => mockRandomUUID(),
}));

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.mock("@/i18n", () => ({
  toggleAppLanguage: () => mockToggleAppLanguage(),
}));

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

jest.mock("@/features/sales/ui", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    resolveSalesLocale: () => "en",
    SalesScreen: (props: unknown) => {
      mockSalesScreenProps = props;
      return React.createElement(Text, { testID: "sales-screen" }, "sales");
    },
  };
});

jest.mock("@/features/sales/ui/sales-toolbar-order", () => ({
  reconcileSalesToolbarOrder: (order: readonly string[] | null | undefined) =>
    mockReconcileSalesToolbarOrder(order),
}));

jest.mock("@/ui/preferences/terminal-ui-preferences", () => ({
  readSalesToolbarOrder: () => mockReadSalesToolbarOrder(),
  saveSalesToolbarOrder: (order: readonly string[]) =>
    mockSaveSalesToolbarOrder(order),
}));

jest.mock("@/features/scanner-camera", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    CameraScannerModal: (props: unknown) => {
      mockCameraScannerProps = props;
      return React.createElement(
        Text,
        { testID: "camera-scanner-modal-mock" },
        String((props as { visible: boolean }).visible),
      );
    },
  };
});

jest.mock("@/ui/scanner/scanner-route-bridge", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    RouteHidScannerCapture: (props: unknown) => {
      mockRouteCaptureProps = props;
      return React.createElement(
        Text,
        { testID: "hid-scanner-capture-mock" },
        String((props as { enabled?: boolean }).enabled ?? true),
      );
    },
  };
});

jest.mock("@/features/catalog/maintenance", () => ({
  canDownloadCatalog: (permissions: readonly string[]) =>
    permissions.includes("Permissions.PosTerminal.Settings.CatalogDownload"),
}));

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
  mockSalesScreenProps = null;
  mockCameraScannerProps = null;
  mockRouteCaptureProps = null;
  mockHasRecoveryRequired.mockResolvedValue(false);
  mockAddLookupCode.mockResolvedValue(true);
  mockRandomUUID.mockReturnValue("123e4567-e89b-42d3-a456-426614174000");
  mockToggleAppLanguage.mockResolvedValue("zh");
  mockReadSalesToolbarOrder.mockReturnValue(null);
  mockSaveSalesToolbarOrder.mockResolvedValue(undefined);
  mockReconcileSalesToolbarOrder.mockImplementation((order) =>
    order ? [...order] : [...DEFAULT_TOOLBAR_ORDER],
  );
  mockActiveCashier = {
    cashierId: "C1",
    cashierName: "Cashier",
    userGuid: "user-1",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    permissions: [
      "Permissions.PosTerminal.CashDrawer.Open",
      "Permissions.PosTerminal.History.View",
      "Permissions.PosTerminal.SpecialProducts.View",
      "Permissions.PosTerminal.DailyClose.View",
      "Permissions.PosTerminal.Installments.View",
      "Permissions.PosTerminal.Settings.View",
    ],
    source: "online",
  };
  mockCreatePresenter.mockReturnValue({
    addLookupCode: mockAddLookupCode,
    destroy: mockDestroyPresenter,
    getState: () => ({
      cart: {
        revision: 7,
        lines: [{ lineId: "line-1" }],
        actualAmount: { currency: "AUD", cents: 1_250 },
      },
    }),
    setQuery: mockSetQuery,
  });
  mockUpdateGate = {
    state: "enabled",
    canStartNewTransaction: true,
    canContinueRecovery: true,
  };
  mockUpdatePolicy = {
    enabled: true,
    minimumSupportedVersion: null,
    latestVersion: null,
    forceUpdate: false,
    appStoreUrl: null,
    releaseMessage: null,
  };
  mockRuntime = readyRuntime();
});

afterEach(() => {
  jest.useRealTimers();
});

test("销售路由零参数创建生产 presenter，并在卸载时销毁", async () => {
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });

  expect(mockCreatePresenter).toHaveBeenCalledTimes(1);
  expect(mockCreatePresenter).toHaveBeenCalledWith();

  mockSalesScreenProps.onOpenHeldOrders();
  expect(mockRouterPush).toHaveBeenCalledWith("/held-orders");
  mockSalesScreenProps.onOpenDailyClose();
  expect(mockRouterPush).toHaveBeenCalledWith("/daily-close");
  mockSalesScreenProps.onOpenReturns();
  expect(mockRouterPush).toHaveBeenCalledWith("/returns");
  mockSalesScreenProps.onOpenRemoteHistory();
  expect(mockRouterPush).toHaveBeenCalledWith("/remote-history");
  mockSalesScreenProps.onOpenSpecialProducts();
  expect(mockRouterPush).toHaveBeenCalledWith("/special-products");
  mockSalesScreenProps.onOpenInstallments();
  expect(mockRouterPush).toHaveBeenCalledWith("/installments");
  mockSalesScreenProps.onOpenSettings();
  expect(mockRouterPush).toHaveBeenCalledWith("/settings");
  mockSalesScreenProps.onOpenSyncHistory();
  expect(mockRouterPush).toHaveBeenCalledWith("/sync-history");
  mockSalesScreenProps.onOpenPayment();
  expect(mockRouterPush).toHaveBeenCalledWith({
    pathname: "/payment",
    params: {
      checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
      revision: "7",
      totalCents: "1250",
    },
  });
  expect(mockSalesScreenProps.onOpenCatalogMaintenance).toBeUndefined();
  expect(mockSalesScreenProps.newTransactionGate).toEqual(mockUpdateGate);
  mockSalesScreenProps.onSwitchLanguage();
  expect(mockToggleAppLanguage).toHaveBeenCalledTimes(1);

  await screen.unmount();
  expect(mockDestroyPresenter).toHaveBeenCalledTimes(1);
});

test("销售工具栏同步读取已保存顺序，先更新页面再异步持久化", async () => {
  const storedOrder = ["lock", "language", "hold"];
  const updatedOrder = ["language", "hold", "lock"];
  mockReadSalesToolbarOrder.mockReturnValue(storedOrder);
  mockReconcileSalesToolbarOrder.mockImplementation((order) =>
    order ? [...order] : [...DEFAULT_TOOLBAR_ORDER],
  );
  mockSaveSalesToolbarOrder.mockRejectedValueOnce(
    new Error("terminal preferences unavailable"),
  );

  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });
  expect(mockReadSalesToolbarOrder).toHaveBeenCalledTimes(1);
  expect(mockReconcileSalesToolbarOrder).toHaveBeenCalledWith(storedOrder);
  expect(mockSalesScreenProps.toolbarOrder).toEqual(storedOrder);

  await act(async () => {
    mockSalesScreenProps.onToolbarOrderChange(updatedOrder);
    await Promise.resolve();
  });

  expect(mockSaveSalesToolbarOrder).toHaveBeenCalledWith(updatedOrder);
  expect(mockSalesScreenProps.toolbarOrder).toEqual(updatedOrder);
  expect(mockReconcileSalesToolbarOrder).toHaveBeenLastCalledWith(updatedOrder);

  await screen.unmount();
});

test("相机扫码复用商品上下文，打开期间停用 HID 且只触发一次加购", async () => {
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });
  expect(mockCameraScannerProps.visible).toBe(false);
  expect(mockRouteCaptureProps.enabled).toBe(true);

  await act(async () => {
    mockSalesScreenProps.onOpenCameraScanner();
  });

  expect(mockCameraScannerProps.visible).toBe(true);
  expect(mockRouteCaptureProps.enabled).toBe(false);
  expect(mockAcquireScannerContext).toHaveBeenCalledWith("product");

  await act(async () => {
    await mockCameraScannerProps.onScan(" 9300000000012 ");
  });
  expect(mockSetQuery).toHaveBeenCalledWith(" 9300000000012 ");
  expect(mockAddLookupCode).toHaveBeenCalledTimes(1);

  await act(async () => {
    mockCameraScannerProps.onClose();
  });
  expect(mockCameraScannerProps.visible).toBe(false);
  expect(mockRouteCaptureProps.enabled).toBe(true);
  expect(mockReleaseCameraContext).toHaveBeenCalledTimes(1);
});

test("手动输入失焦后延后恢复 HID，焦点交接会取消恢复", async () => {
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });
  jest.useFakeTimers();
  try {
    expect(mockRouteCaptureProps.enabled).toBe(true);

    await act(async () => {
      mockSalesScreenProps.onManualInputFocusChange(true);
    });
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockSalesScreenProps.onManualInputFocusChange(false);
    });
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockSalesScreenProps.onManualInputFocusChange(true);
      jest.runOnlyPendingTimers();
    });
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockSalesScreenProps.onManualInputFocusChange(false);
      jest.runOnlyPendingTimers();
    });
    expect(mockRouteCaptureProps.enabled).toBe(true);
  } finally {
    jest.useRealTimers();
  }
});

test("相机扫码期间不恢复 HID，关闭后才恢复", async () => {
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });
  jest.useFakeTimers();
  try {
    await act(async () => {
      mockSalesScreenProps.onManualInputFocusChange(true);
      mockSalesScreenProps.onManualInputFocusChange(false);
      mockSalesScreenProps.onOpenCameraScanner();
      jest.runOnlyPendingTimers();
    });
    expect(mockCameraScannerProps.visible).toBe(true);
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockCameraScannerProps.onClose();
    });
    expect(mockRouteCaptureProps.enabled).toBe(true);
  } finally {
    jest.useRealTimers();
  }
});

test("销售路由卸载时清理待恢复的 HID 定时器", async () => {
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });
  jest.useFakeTimers();
  const clearTimeoutSpy = jest.spyOn(global, "clearTimeout");
  try {
    await act(async () => {
      mockSalesScreenProps.onManualInputFocusChange(true);
      mockSalesScreenProps.onManualInputFocusChange(false);
    });

    await screen.unmount();
    expect(clearTimeoutSpy).toHaveBeenCalledTimes(1);

    await act(async () => {
      jest.runOnlyPendingTimers();
    });
  } finally {
    clearTimeoutSpy.mockRestore();
    jest.useRealTimers();
  }
});

test("只有冻结权限摘要包含目录下载权限时才暴露维护入口", async () => {
  mockActiveCashier.permissions = [
    "Permissions.PosTerminal.Settings.CatalogDownload",
  ];
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("sales-screen")).toBeTruthy();
  });
  mockSalesScreenProps.onOpenCatalogMaintenance();
  expect(mockRouterPush).toHaveBeenCalledWith("/catalog-maintenance");
  expect(mockSalesScreenProps.onOpenRemoteHistory).toBeUndefined();
  expect(mockSalesScreenProps.onOpenSpecialProducts).toBeUndefined();
  expect(mockSalesScreenProps.onOpenInstallments).toBeUndefined();
  expect(mockSalesScreenProps.onOpenSettings).toBeUndefined();
});

test("presenter 创建失败时清除活动收银员并安全返回登录", async () => {
  mockCreatePresenter.mockImplementationOnce(() => {
    throw new Error("current cashier is invalid");
  });
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(mockClearActiveCashier).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("redirect").props.children).toBe("/login");
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith();
});

test("销售直链没有收银员会话时返回登录页", async () => {
  mockActiveCashier = null;
  const screen = await render(<SalesRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/login");
  expect(mockCreatePresenter).not.toHaveBeenCalled();
});

test("检测到冷恢复时先转到支付页，不创建销售 presenter", async () => {
  mockUpdateGate = {
    state: "force-update",
    canStartNewTransaction: false,
    canContinueRecovery: true,
  };
  mockHasRecoveryRequired.mockResolvedValue(true);
  const screen = await render(<SalesRoute />);

  await waitFor(() => {
    expect(mockRouterReplace).toHaveBeenCalledWith("/payment");
  });
  expect(mockCreatePresenter).not.toHaveBeenCalled();
  expect(screen.getByTestId("bootstrap")).toBeTruthy();
});

function readyRuntime() {
  return {
    state: { phase: "ready", device: "authorized-online" },
    services: {
      sales: { createPresenter: mockCreatePresenter },
      payments: {
        status: "available",
        createPresenter: jest.fn(),
        hasRecoveryRequired: mockHasRecoveryRequired,
      },
      scanner: {
        router: {
          acceptCameraText: jest.fn(),
          acquireContext: mockAcquireScannerContext,
          startCamera: jest.fn(),
          stopCamera: jest.fn(),
        },
      },
      appUpdates: {
        getGate: () => mockUpdateGate,
        getPolicy: () => mockUpdatePolicy,
        subscribe: (listener: (gate: unknown) => void) => {
          listener(mockUpdateGate);
          return () => undefined;
        },
      },
    },
  };
}
