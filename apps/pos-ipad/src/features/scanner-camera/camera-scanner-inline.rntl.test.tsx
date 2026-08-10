/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { act, fireEvent, render, waitFor } from "@testing-library/react-native";

let mockLanguage: "en" | "zh" = "en";

jest.doMock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: mockLanguage, resolvedLanguage: mockLanguage },
  }),
}));

jest.doMock("expo-camera", () => {
  const React = require("react");
  const { View } = require("react-native");
  const CameraView = (props: Record<string, unknown>) =>
    React.createElement(View, { ...props, testID: "camera-scanner-preview" });
  CameraView.isAvailableAsync = jest
    .fn<() => Promise<boolean>>()
    .mockResolvedValue(true);
  return {
    CameraView,
    useCameraPermissions: jest.fn(() => [
      { granted: true, status: "granted" },
      jest.fn(),
    ]),
  };
});

const { CameraScannerInline: InlineUnderTest } = require("./index") as typeof import("./index");
type CameraScannerPort = import("./index").CameraScannerPort;

test("连续内联扫码串行提交，并在同码静默 1200ms 后解锁", async () => {
  jest.useFakeTimers();
  const scanner = new CameraScannerPortStub();
  const firstSubmission = deferred<boolean>();
  const onScan = jest.fn((value: string) => {
    if (value === "ABC") return firstSubmission.promise;
    return Promise.resolve(true);
  });
  const rendered = await render(
    <InlineUnderTest
      context="product"
      onClose={jest.fn()}
      onScan={onScan}
      scanner={scanner}
      visible
    />,
  );

  const preview = await rendered.findByTestId("camera-scanner-preview");
  await waitFor(() => expect(scanner.startCalls).toBe(1));

  await fireEvent(preview, "onBarcodeScanned", { data: "  ABC\r\n" });
  await fireEvent(preview, "onBarcodeScanned", { data: "DEF" });

  expect(scanner.accepted).toEqual(["ABC", "DEF"]);
  expect(onScan).toHaveBeenCalledTimes(1);
  expect(rendered.getByText("Verifying barcode")).toBeTruthy();

  await act(async () => {
    firstSubmission.resolve(true);
    await Promise.resolve();
  });

  await waitFor(() => expect(onScan).toHaveBeenCalledTimes(2));
  expect(scanner.accepted).toEqual(["ABC", "DEF"]);
  expect(rendered.getByText("Barcode submitted")).toBeTruthy();

  await act(async () => {
    await Promise.resolve();
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "ABC" });
  await act(async () => {
    jest.advanceTimersByTime(900);
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "ABC" });
  await act(async () => {
    jest.advanceTimersByTime(1199);
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "ABC" });

  expect(onScan).toHaveBeenCalledTimes(2);

  await act(async () => {
    jest.advanceTimersByTime(1200);
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "ABC" });

  await waitFor(() => expect(onScan).toHaveBeenCalledTimes(3));
  expect(scanner.accepted).toEqual(["ABC", "DEF", "ABC"]);
  expect(InlineUnderTest).toEqual(expect.any(Function));

  await rendered.unmount();
  jest.useRealTimers();
});

test("连续内联扫码显示提交结果，关闭后清理计时器并忽略迟到 promise", async () => {
  jest.useFakeTimers();
  mockLanguage = "zh";
  const scanner = new CameraScannerPortStub();
  const okSubmission = deferred<boolean>();
  const slowSubmission = deferred<boolean>();
  const onScan = jest.fn((value: string) => {
    if (value === "OK") return okSubmission.promise;
    if (value === "FAIL") return false;
    if (value === "THROW") throw new Error("submit failed");
    if (value === "SLOW") return slowSubmission.promise;
    return true;
  });
  const onClose = jest.fn();
  const rendered = await render(
    <InlineUnderTest
      context="product-search"
      onClose={onClose}
      onScan={onScan}
      scanner={scanner}
      visible
    />,
  );
  const preview = await rendered.findByTestId("camera-scanner-preview");
  await waitFor(() => expect(scanner.startCalls).toBe(1));

  await fireEvent(preview, "onBarcodeScanned", { data: "OK" });
  expect(rendered.getByText("正在核验")).toBeTruthy();
  await act(async () => {
    okSubmission.resolve(true);
    await Promise.resolve();
  });
  expect(rendered.getByText("条码已提交")).toBeTruthy();

  await fireEvent(preview, "onBarcodeScanned", { data: "FAIL" });
  await act(async () => {
    await Promise.resolve();
  });
  expect(rendered.getByText("未能提交")).toBeTruthy();

  await fireEvent(preview, "onBarcodeScanned", { data: "THROW" });
  await act(async () => {
    await Promise.resolve();
  });
  expect(rendered.getByText("未能提交")).toBeTruthy();

  await fireEvent(preview, "onBarcodeScanned", { data: "SLOW" });
  expect(rendered.getByText("正在核验")).toBeTruthy();
  fireEvent.press(rendered.getByTestId("camera-scanner-inline-close"));
  expect(onClose).toHaveBeenCalledTimes(1);
  await waitFor(() => expect(scanner.stopCalls).toBe(1));

  await act(async () => {
    slowSubmission.resolve(false);
    jest.runOnlyPendingTimers();
    await Promise.resolve();
  });
  expect(rendered.queryByText("未能提交")).toBeNull();

  await rendered.unmount();
  jest.useRealTimers();
});

test("提交耗时超过锁窗口时，同码持续出现仍延后解锁", async () => {
  jest.useFakeTimers();
  mockLanguage = "en";
  const scanner = new CameraScannerPortStub();
  const firstSubmission = deferred<boolean>();
  const onScan = jest.fn((value: string) => {
    if (value === "SLOW") return firstSubmission.promise;
    return true;
  });
  const rendered = await render(
    <InlineUnderTest
      context="product"
      onClose={jest.fn()}
      onScan={onScan}
      scanner={scanner}
      visible
    />,
  );
  const preview = await rendered.findByTestId("camera-scanner-preview");
  await waitFor(() => expect(scanner.startCalls).toBe(1));

  await fireEvent(preview, "onBarcodeScanned", { data: "SLOW" });
  await act(async () => {
    jest.advanceTimersByTime(1300);
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "SLOW" });
  expect(onScan).toHaveBeenCalledTimes(1);

  await act(async () => {
    firstSubmission.resolve(true);
    await Promise.resolve();
  });
  await act(async () => {
    jest.advanceTimersByTime(1199);
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "SLOW" });
  expect(onScan).toHaveBeenCalledTimes(1);

  await act(async () => {
    jest.advanceTimersByTime(1200);
  });
  await fireEvent(preview, "onBarcodeScanned", { data: "SLOW" });
  expect(onScan).toHaveBeenCalledTimes(2);

  await rendered.unmount();
  jest.useRealTimers();
});

test("旧启动迟到完成不会关闭已经重开的新相机会话", async () => {
  const scanner = new DeferredStartCameraScannerPortStub();
  const props = {
    context: "product" as const,
    onClose: jest.fn(),
    onScan: jest.fn(async (_value: string) => true),
    scanner,
  };
  const rendered = await render(<InlineUnderTest {...props} visible />);
  await waitFor(() => expect(scanner.startCalls).toBe(1));

  await rendered.rerender(<InlineUnderTest {...props} visible={false} />);
  await rendered.rerender(<InlineUnderTest {...props} visible />);
  await waitFor(() => expect(scanner.startCalls).toBe(2));

  await act(async () => {
    scanner.resolveStart(1);
    await Promise.resolve();
  });
  await rendered.findByTestId("camera-scanner-preview");
  expect(scanner.active).toBe(true);
  const stopCallsAfterReopen = scanner.stopCalls;

  await act(async () => {
    scanner.resolveStart(0);
    await Promise.resolve();
  });

  expect(scanner.active).toBe(true);
  expect(scanner.stopCalls).toBe(stopCallsAfterReopen);
  await rendered.unmount();
});

test("运行时替换扫码器实例后仍使用新实例提交并更新结果", async () => {
  mockLanguage = "en";
  const firstScanner = new CameraScannerPortStub();
  const nextScanner = new CameraScannerPortStub();
  const onScan = jest.fn(async (_value: string) => true);
  const onClose = jest.fn();
  const rendered = await render(
    <InlineUnderTest
      context="product"
      onClose={onClose}
      onScan={onScan}
      scanner={firstScanner}
      visible
    />,
  );
  await waitFor(() => expect(firstScanner.startCalls).toBe(1));

  await rendered.rerender(
    <InlineUnderTest
      context="product"
      onClose={onClose}
      onScan={onScan}
      scanner={nextScanner}
      visible
    />,
  );
  await waitFor(() => expect(nextScanner.startCalls).toBe(1));

  await fireEvent(
    rendered.getByTestId("camera-scanner-preview"),
    "onBarcodeScanned",
    { data: "NEXT" },
  );

  await waitFor(() => expect(onScan).toHaveBeenCalledWith("NEXT"));
  await waitFor(() =>
    expect(rendered.getByText("Barcode submitted")).toBeTruthy(),
  );
  expect(firstScanner.accepted).toEqual([]);
  expect(nextScanner.accepted).toEqual(["NEXT"]);

  await rendered.unmount();
});

class CameraScannerPortStub implements CameraScannerPort {
  public accepted: string[] = [];
  public startCalls = 0;
  public stopCalls = 0;

  public acceptCameraText(value: string): boolean {
    this.accepted.push(value);
    return true;
  }

  public async startCamera(): Promise<void> {
    this.startCalls += 1;
  }

  public async stopCamera(): Promise<void> {
    this.stopCalls += 1;
  }
}

class DeferredStartCameraScannerPortStub implements CameraScannerPort {
  public active = false;
  public stopCalls = 0;
  private readonly starts: ReturnType<typeof deferred<void>>[] = [];

  public get startCalls(): number {
    return this.starts.length;
  }

  public acceptCameraText(): boolean {
    return this.active;
  }

  public startCamera(): Promise<void> {
    const start = deferred<void>();
    this.starts.push(start);
    return start.promise.then(() => {
      this.active = true;
    });
  }

  public async stopCamera(): Promise<void> {
    this.stopCalls += 1;
    this.active = false;
  }

  public resolveStart(index: number): void {
    this.starts[index]?.resolve(undefined);
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((resolver) => {
    resolve = resolver;
  });
  return { promise, resolve };
}
