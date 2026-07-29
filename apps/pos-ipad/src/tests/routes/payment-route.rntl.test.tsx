import { beforeEach, expect, jest, test } from "@jest/globals";
import { render, waitFor } from "@testing-library/react-native";

import PaymentRoute from "../../../app/payment";

let mockRuntime: any;
let mockActiveCashier: any;
let mockParams: any;
let mockPaymentScreenProps: any;
const mockClearActiveCashier = jest.fn();
const mockCreateRegularPresenter = jest.fn();
const mockCreateInstallmentPresenter = jest.fn();
const mockRegularRecoveryRequired = jest.fn<() => Promise<boolean>>();
const mockInstallmentRecoveryRequired =
  jest.fn<() => Promise<boolean>>();
const mockDestroyRegularPresenter = jest.fn();
const mockDestroyInstallmentPresenter = jest.fn();
const mockRouterReplace = jest.fn();

jest.mock("expo-router", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    Redirect: ({ href }: { href: string }) =>
      React.createElement(Text, { testID: "redirect" }, href),
    // Expo Router 每次 render 都会 Object.fromEntries；返回新对象以锁住 effect 稳定性。
    useLocalSearchParams: () =>
      Object.fromEntries(
        Object.entries(mockParams).map(([key, value]) => [
          key,
          Array.isArray(value) ? [...value] : value,
        ]),
      ),
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

jest.mock("@/features/installments", () => ({
  resolveInstallmentsRuntimeFactory: (services: any) => {
    const candidate = services.installments;
    return candidate &&
      typeof candidate.prepareCreateCheckout === "function" &&
      typeof candidate.createCheckoutPresenter === "function" &&
      typeof candidate.hasRecoveryRequired === "function"
      ? candidate
      : null;
  },
}));

jest.mock("@/features/payments/ui", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  const actual =
    jest.requireActual<typeof import("@/features/payments/ui")>(
      "@/features/payments/ui",
    );
  return {
    ...actual,
    resolvePaymentLocale: () => "en",
    PaymentScreen: (props: unknown) => {
      mockPaymentScreenProps = props;
      return React.createElement(
        Text,
        { testID: "payment-screen" },
        "payment",
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
  mockRegularRecoveryRequired.mockResolvedValue(false);
  mockInstallmentRecoveryRequired.mockResolvedValue(false);
  mockCreateRegularPresenter.mockReturnValue({
    destroy: mockDestroyRegularPresenter,
  });
  mockCreateInstallmentPresenter.mockReturnValue({
    destroy: mockDestroyInstallmentPresenter,
  });
  mockRuntime = readyRuntime();
});

test("普通交易经统一 facade 只传最小 checkout entry", async () => {
  const screen = await render(<PaymentRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockRegularRecoveryRequired).toHaveBeenCalledTimes(1);
  expect(mockInstallmentRecoveryRequired).toHaveBeenCalledTimes(1);
  expect(mockCreateRegularPresenter).toHaveBeenCalledWith({
    kind: "regular",
    checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
    expectedCartRevision: 7,
    total: { currency: "AUD", cents: 1_250 },
    lines: [],
  });
  expect(mockCreateRegularPresenter).toHaveBeenCalledTimes(1);
  expect(mockRuntime.services.payments.attempts).toBeUndefined();
  expect(mockRuntime.services.payments.providers).toBeUndefined();

  mockPaymentScreenProps.onBack();
  mockPaymentScreenProps.onComplete("order-not-used-by-route");
  expect(mockRouterReplace).toHaveBeenNthCalledWith(1, "/sales");
  expect(mockRouterReplace).toHaveBeenNthCalledWith(2, "/sales");
});

test("新建分期和续付参数分别创建对应统一支付 presenter", async () => {
  mockParams = {
    flow: "installment-create",
    checkoutIntentId: "223e4567-e89b-42d3-a456-426614174000",
    revision: "8",
  };
  const create = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(create.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreateInstallmentPresenter).toHaveBeenCalledWith({
    kind: "installment-create",
    checkoutIntentId: "223e4567-e89b-42d3-a456-426614174000",
    expectedCartRevision: 8,
  });
  await create.unmount();

  jest.clearAllMocks();
  mockRegularRecoveryRequired.mockResolvedValue(false);
  mockInstallmentRecoveryRequired.mockResolvedValue(false);
  mockCreateInstallmentPresenter.mockReturnValue({
    destroy: mockDestroyInstallmentPresenter,
  });
  mockParams = {
    flow: "installment-repayment",
    installmentGuid: "323e4567-e89b-42d3-a456-426614174000",
  };
  const repayment = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(repayment.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreateInstallmentPresenter).toHaveBeenCalledWith({
    kind: "installment-repayment",
    installmentGuid: "323e4567-e89b-42d3-a456-426614174000",
  });
  await repayment.unmount();
});

test("两账本同时阻塞时固定优先恢复普通支付", async () => {
  mockRegularRecoveryRequired.mockResolvedValue(true);
  mockInstallmentRecoveryRequired.mockResolvedValue(true);
  const screen = await render(<PaymentRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreateRegularPresenter).toHaveBeenCalledWith(null);
  expect(mockCreateInstallmentPresenter).not.toHaveBeenCalled();
});

test("只有分期阻塞时恢复分期账本，无恢复且无参数则返回销售页", async () => {
  mockParams = {};
  mockInstallmentRecoveryRequired.mockResolvedValue(true);
  const recovery = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(recovery.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreateInstallmentPresenter).toHaveBeenCalledWith(null);
  expect(mockCreateRegularPresenter).not.toHaveBeenCalled();
  await recovery.unmount();

  jest.clearAllMocks();
  mockRegularRecoveryRequired.mockResolvedValue(false);
  mockInstallmentRecoveryRequired.mockResolvedValue(false);
  const none = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(none.getByTestId("redirect").props.children).toBe("/sales");
  });
  expect(mockCreateRegularPresenter).not.toHaveBeenCalled();
  expect(mockCreateInstallmentPresenter).not.toHaveBeenCalled();
});

test.each([
  [
    "未知参数",
    {
      checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
      revision: "7",
      totalCents: "1250",
      injected: "true",
    },
  ],
  [
    "数组参数",
    {
      flow: ["regular", "installment-create"],
      checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
      revision: "7",
      totalCents: "1250",
    },
  ],
  [
    "不安全整数",
    {
      checkoutIntentId: "123e4567-e89b-42d3-a456-426614174000",
      revision: "9007199254740992",
      totalCents: "1250",
    },
  ],
  [
    "非 UUID",
    {
      flow: "installment-repayment",
      installmentGuid: "../../payment",
    },
  ],
])("严格拒绝%s并且不读取任何恢复账本", async (_label, params) => {
  mockParams = params;
  const screen = await render(<PaymentRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/sales");
  expect(mockRegularRecoveryRequired).not.toHaveBeenCalled();
  expect(mockInstallmentRecoveryRequired).not.toHaveBeenCalled();
  expect(mockCreateRegularPresenter).not.toHaveBeenCalled();
  expect(mockCreateInstallmentPresenter).not.toHaveBeenCalled();
});

test("两条 capability 独立降级，不因另一账本缺失禁用可用入口", async () => {
  delete mockRuntime.services.installments;
  const regular = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(regular.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreateRegularPresenter).toHaveBeenCalledTimes(1);
  await regular.unmount();

  jest.clearAllMocks();
  mockParams = {
    flow: "installment-create",
    checkoutIntentId: "223e4567-e89b-42d3-a456-426614174000",
    revision: "8",
  };
  mockRuntime = readyRuntime();
  mockRuntime.services.payments = {
    status: "unavailable",
    blockers: ["PAYMENTS_DISABLED"],
  };
  mockInstallmentRecoveryRequired.mockResolvedValue(false);
  mockCreateInstallmentPresenter.mockReturnValue({
    destroy: mockDestroyInstallmentPresenter,
  });
  const installment = await render(<PaymentRoute />);
  await waitFor(() => {
    expect(installment.getByTestId("payment-screen")).toBeTruthy();
  });
  expect(mockCreateInstallmentPresenter).toHaveBeenCalledTimes(1);
  await installment.unmount();
});

test("未登录直链返回登录，且不会读取支付运行时", async () => {
  mockActiveCashier = null;
  const screen = await render(<PaymentRoute />);

  expect(screen.getByTestId("redirect").props.children).toBe("/login");
  expect(mockRegularRecoveryRequired).not.toHaveBeenCalled();
  expect(mockInstallmentRecoveryRequired).not.toHaveBeenCalled();
  expect(mockCreateRegularPresenter).not.toHaveBeenCalled();
});

test("请求的账本不可用时 fail closed 回销售页", async () => {
  mockRuntime.services.payments = {
    status: "unavailable",
    blockers: ["PAYMENTS_DISABLED"],
  };
  const screen = await render(<PaymentRoute />);

  await waitFor(() => {
    expect(screen.getByTestId("redirect").props.children).toBe("/sales");
  });
  expect(mockCreateRegularPresenter).not.toHaveBeenCalled();
});

function readyRuntime() {
  return {
    state: { phase: "ready", device: "authorized-online" },
    services: {
      payments: {
        status: "available",
        createPresenter: mockCreateRegularPresenter,
        hasRecoveryRequired: mockRegularRecoveryRequired,
      },
      installments: {
        prepareCreateCheckout: jest.fn(),
        createCheckoutPresenter: mockCreateInstallmentPresenter,
        hasRecoveryRequired: mockInstallmentRecoveryRequired,
      },
    },
  };
}
