import { beforeEach, expect, jest, test } from "@jest/globals";
import { render, waitFor } from "@testing-library/react-native";

import PaymentRoute from "../../../app/payment";

let mockRuntime: any;
let mockActiveCashier: any;
let mockParams: any;
let mockPaymentScreenProps: any;
const mockClearActiveCashier = jest.fn();
const mockCreatePresenter = jest.fn();
const mockHasRecoveryRequired = jest.fn<() => Promise<boolean>>();
const mockDestroyPresenter = jest.fn();
const mockRouterReplace = jest.fn();

jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    Redirect: ({ href }: { href: string }) =>
      React.createElement(Text, { testID: "redirect" }, href),
    useLocalSearchParams: () => mockParams,
    useRouter: () => ({ replace: mockRouterReplace }),
  };
});

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
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

jest.mock("@/features/payments/ui", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    resolvePaymentLocale: () => "en",
    PaymentScreen: (props: unknown) => {
      mockPaymentScreenProps = props;
      return React.createElement(Text, { testID: "payment-screen" }, "payment");
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
  mockParams = {
    checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
    revision: "7",
    totalCents: "1250",
  };
  mockPaymentScreenProps = null;
  mockActiveCashier = {
    cashierId: "C1",
    cashierName: "Cashier",
    userGuid: "user-1",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    permissions: [],
    source: "online",
  };
  mockHasRecoveryRequired.mockResolvedValue(false);
  mockCreatePresenter.mockReturnValue({ destroy: mockDestroyPresenter });
  mockRuntime = readyRuntime();
});

test("新交易只把 checkout intent、revision 和金额传给窄 presenter 工厂", async () => {
  const screen = await render(<PaymentRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith({
    checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
    expectedCartRevision: 7,
    total: { currency: "AUD", cents: 1_250 },
  });
  expect(mockRuntime.services.payments.attempts).toBeUndefined();
  expect(mockRuntime.services.payments.providers).toBeUndefined();

  mockPaymentScreenProps.onBack();
  mockPaymentScreenProps.onComplete("order-not-used-by-route");
  expect(mockRouterReplace).toHaveBeenNthCalledWith(1, "/sales");
  expect(mockRouterReplace).toHaveBeenNthCalledWith(2, "/sales");
});

test("无交易参数以恢复入口创建 presenter", async () => {
  mockParams = {};
  const screen = await render(<PaymentRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith(null);
});

test("存在冷恢复时忽略旧的新交易参数并创建恢复入口", async () => {
  mockHasRecoveryRequired.mockResolvedValue(true);
  const screen = await render(<PaymentRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreatePresenter).toHaveBeenCalledWith(null);
});

test("未登录直链返回登录，且不会读取支付运行时", async () => {
  mockActiveCashier = null;
  const screen = await render(<PaymentRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/login");
  expect(mockHasRecoveryRequired).not.toHaveBeenCalled();
  expect(mockCreatePresenter).not.toHaveBeenCalled();
});

test("支付不可用或参数不完整时 fail closed 回销售页", async () => {
  mockRuntime.services.payments = {
    status: "unavailable",
    blockers: ["PAYMENTS_DISABLED"],
  };
  const unavailable = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(unavailable.getByTestId("redirect").props.children).toBe("/sales");
  });
  expect(mockCreatePresenter).not.toHaveBeenCalled();

  mockParams = { checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000" };
  const invalid = await render(<PaymentRoute />);
  expect(invalid.getByTestId("redirect").props.children).toBe("/sales");
  expect(mockHasRecoveryRequired).not.toHaveBeenCalled();
});

function readyRuntime() {
  return {
    state: { phase: "ready", device: "authorized-online" },
    services: {
      payments: {
        status: "available",
        createPresenter: mockCreatePresenter,
        hasRecoveryRequired: mockHasRecoveryRequired,
      },
    },
  };
}
