import { expect, jest, test } from "@jest/globals";
import { act, render } from "@testing-library/react-native";

import LoginRoute from "../../../app/login";

let mockRuntime: any;
let mockRouteCaptureProps: any;
let mockScreenProps: any;
const mockRouterReplace = jest.fn();

jest.mock("expo-router", () => ({
  router: { replace: (...args: unknown[]) => mockRouterReplace(...args) },
}));

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

jest.mock("@/features/cashier-login", () => {
  const React = jest.requireActual<typeof import("react")>("react");
  const { Text } =
    jest.requireActual<typeof import("react-native")>("react-native");
  return {
    CashierLoginScreen: (props: unknown) => {
      mockScreenProps = props;
      return React.createElement(Text, { testID: "cashier-login" }, "login");
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
        { testID: "login-hid-capture" },
        String((props as { enabled?: boolean }).enabled ?? true),
      );
    },
  };
});

test("登录路由只传受限 runtime facade，成功后替换为销售页", async () => {
  mockRuntime = {
    state: { phase: "ready", device: "authorized-online" },
    services: { cashierSession: { signIn: jest.fn() } },
  };
  mockRouteCaptureProps = null;
  mockScreenProps = null;
  const screen = await render(<LoginRoute />);

  expect(screen.getByTestId("cashier-login")).toBeTruthy();
  expect(screen.getByTestId("login-hid-capture").props.children).toBe("false");
  expect(mockRouteCaptureProps.enabled).toBe(false);
  expect(mockScreenProps).toMatchObject({
    language: "en",
    runtime: mockRuntime,
  });
  expect(mockScreenProps).not.toHaveProperty("storeCode");
  expect(mockScreenProps).not.toHaveProperty("deviceCode");
  expect(mockScreenProps).not.toHaveProperty("token");

  await act(async () => {
    mockScreenProps.onManualInputFocusChange(true);
  });
  expect(mockRouteCaptureProps.enabled).toBe(false);

  mockScreenProps.onSuccess();
  expect(mockRouterReplace).toHaveBeenCalledWith("/sales");
});

test("可见输入失焦后延迟恢复 HID，快速重新聚焦会取消恢复", async () => {
  mockRouteCaptureProps = null;
  mockScreenProps = null;
  const screen = await render(<LoginRoute />);

  jest.useFakeTimers();
  try {
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockScreenProps.onManualInputFocusChange(false);
    });
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockScreenProps.onManualInputFocusChange(true);
      jest.runOnlyPendingTimers();
    });
    expect(mockRouteCaptureProps.enabled).toBe(false);

    await act(async () => {
      mockScreenProps.onManualInputFocusChange(false);
      jest.runOnlyPendingTimers();
    });
    expect(mockRouteCaptureProps.enabled).toBe(true);
    expect(screen.getByTestId("login-hid-capture").props.children).toBe("true");
  } finally {
    jest.useRealTimers();
  }
});

test("登录路由卸载时清理待恢复的 HID 定时器", async () => {
  mockRouteCaptureProps = null;
  mockScreenProps = null;
  const screen = await render(<LoginRoute />);

  jest.useFakeTimers();
  const clearTimeoutSpy = jest.spyOn(global, "clearTimeout");
  try {
    await act(async () => {
      mockScreenProps.onManualInputFocusChange(false);
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
