import { expect, jest, test } from "@jest/globals";
import { act, render, waitFor } from "@testing-library/react-native";

import { HidScannerRouter } from "@/core/peripherals/scanner";
import {
  RouteHidScannerCapture,
  ScannerRouteProvider,
} from "@/ui/scanner/scanner-route-bridge";

let mockRuntime: any;
let mockPathname = "/sales";

jest.mock("expo-router", () => ({
  usePathname: () => mockPathname,
}));

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntime,
}));

test("路由切换和主管弹窗只把完整 HID 条码交给当前 context，并在关闭后恢复销售焦点", async () => {
  const scanner = new HidScannerRouter();
  const listeners = new Set<() => void>();
  let authorizationState: { kind: "awaiting-supervisor" | "idle" } = {
    kind: "idle",
  };
  const onScan = jest.fn<(value: string) => void>();
  mockRuntime = {
    services: {
      operationAuthorization: {
        status: "available",
        getState: () => authorizationState,
        subscribe: (listener: () => void) => {
          listeners.add(listener);
          return () => listeners.delete(listener);
        },
      },
      scanner: { router: scanner },
    },
  };

  const screen = await render(
    <ScannerRouteProvider>
      <RouteHidScannerCapture
        context="product"
        onScan={onScan}
        path="/sales"
      />
    </ScannerRouteProvider>,
  );

  await waitFor(() => {
    scanner.setCaptureActive(true);
    scanner.acceptHidText("SKU-1\n");
    expect(onScan).toHaveBeenLastCalledWith("SKU-1");
  });

  await act(async () => {
    authorizationState = { kind: "awaiting-supervisor" };
    listeners.forEach((listener) => listener());
  });
  scanner.setCaptureActive(true);
  scanner.acceptHidText("SUPERVISOR-1\n");
  expect(onScan).toHaveBeenCalledTimes(1);

  await act(async () => {
    authorizationState = { kind: "idle" };
    listeners.forEach((listener) => listener());
  });
  await waitFor(() => {
    scanner.setCaptureActive(true);
    scanner.acceptHidText("SKU-2\n");
    expect(onScan).toHaveBeenLastCalledWith("SKU-2");
  });

  await screen.unmount();
  scanner.acceptHidText("SKU-3\n");
  expect(onScan).toHaveBeenCalledTimes(2);
});

test("同一次 HID 回车同时触发 keyPress 和 submitEditing 时只提交一次条码", async () => {
  const scanner = new HidScannerRouter();
  const onScan = jest.fn<(value: string) => void>();
  mockRuntime = {
    services: {
      operationAuthorization: {
        status: "available",
        getState: () => ({ kind: "idle" }),
        subscribe: () => () => undefined,
      },
      scanner: { router: scanner },
    },
  };

  const screen = await render(
    <ScannerRouteProvider>
      <RouteHidScannerCapture
        context="product"
        onScan={onScan}
        path="/sales"
      />
    </ScannerRouteProvider>,
  );

  const hidInput = () => {
    const matches = screen.container.queryAll(
      (instance) => instance.props.caretHidden === true,
      { matchDeepestOnly: true },
    );
    expect(matches).toHaveLength(1);
    return matches[0]!;
  };
  scanner.setCaptureActive(true);
  await act(async () => {
    hidInput().props.onChangeText("930000000001");
  });
  await act(async () => {
    hidInput().props.onKeyPress({
      nativeEvent: { key: "Enter" },
    });
    hidInput().props.onSubmitEditing({
      nativeEvent: { text: "930000000001" },
    });
  });

  expect(onScan).toHaveBeenCalledTimes(1);
  expect(onScan).toHaveBeenCalledWith("930000000001");

  await act(async () => {
    hidInput().props.onChangeText("930000000001");
  });
  await act(async () => {
    hidInput().props.onKeyPress({
      nativeEvent: { key: "Enter" },
    });
    hidInput().props.onSubmitEditing({
      nativeEvent: { text: "930000000001" },
    });
  });
  expect(onScan).toHaveBeenCalledTimes(2);
  await screen.unmount();
});
