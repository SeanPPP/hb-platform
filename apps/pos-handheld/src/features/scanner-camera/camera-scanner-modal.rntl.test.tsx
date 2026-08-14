/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

let mockLanguage: "en" | "zh" = "en";

jest.doMock("expo-status-bar", () => ({ StatusBar: () => null }));

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
  return { CameraView, useCameraPermissions: jest.fn(() => [{ granted: true, status: "granted" }, jest.fn()]) };
});

const { CameraScannerModal } = require("./camera-scanner-modal") as typeof import("./camera-scanner-modal");
const { cameraScannerText } = require("./camera-scanner-copy") as typeof import("./camera-scanner-copy");
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

test("相机兜底弹窗使用竖屏单列状态面且关键操作至少 48px", async () => {
  const rendered = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={new CameraScannerPortStub()}
      visible
    />,
  );

  expect(rendered.toJSON()).toMatchObject({
    props: {
      supportedOrientations: ["portrait"],
    },
    type: "Modal",
  });
  expect(rendered.getByTestId("handheld-state-camera-scanner")).toBeTruthy();
  expect(
    StyleSheet.flatten(rendered.getByTestId("camera-scanner-close").props.style)
      .minHeight,
  ).toBeGreaterThanOrEqual(48);
  await rendered.unmount();
});

test("320×568 短屏时内容区可收缩滚动，关闭按钮固定在滚动区外", async () => {
  const rendered = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={new CameraScannerPortStub()}
      visible
    />,
  );

  // RNTL 不计算原生布局，因此验证短屏可达性所依赖的结构与 flex 样式合同。
  const panel = rendered.getByTestId("handheld-state-camera-scanner");
  const scroll = rendered.getByTestId("camera-scanner-scroll");
  const close = rendered.getByTestId("camera-scanner-close");

  expect(StyleSheet.flatten(panel.props.style).maxHeight).toBe("96%");
  expect(StyleSheet.flatten(scroll.props.style)).toMatchObject({
    flexShrink: 1,
    minHeight: 0,
  });
  expect(scroll.parent).toBe(panel);
  expect(close.parent).toBe(panel);
  expect(close.parent).not.toBe(scroll);
  expect(
    StyleSheet.flatten(close.props.style).minHeight,
  ).toBeGreaterThanOrEqual(48);
  await rendered.unmount();
});

test("相机权限系统设置文案不再绑定 iPad 产品名", () => {
  expect(cameraScannerText("en", "permission.denied.body")).toContain(
    "device settings",
  );
  expect(cameraScannerText("zh", "permission.denied.body")).toContain(
    "系统设置",
  );
  expect(
    `${cameraScannerText("en", "permission.denied.body")} ${cameraScannerText("zh", "permission.denied.body")}`,
  ).not.toMatch(/ipad/iu);
});

test("只交付一次规范化条码，并立即关闭相机会话", async () => {
  const scanner = new CameraScannerPortStub();
  const onScan = jest.fn();
  const onClose = jest.fn();
  const rendered = await render(
    <CameraScannerModal
      context="product"
      onClose={onClose}
      onScan={onScan}
      scanner={scanner}
      visible
    />,
  );

  const preview = await rendered.findByTestId("camera-scanner-preview");
  await waitFor(() => expect(scanner.startCalls).toBe(1));
  await fireEvent(preview, "onBarcodeScanned", { data: "  01ABC\r\n" });
  await fireEvent(preview, "onBarcodeScanned", { data: "SECOND" });

  expect(scanner.accepted).toEqual(["01ABC"]);
  expect(onScan).toHaveBeenCalledWith("01ABC");
  expect(onClose).toHaveBeenCalledTimes(1);
  await waitFor(() => expect(scanner.stopCalls).toBe(1));
});

test("相机上下文和辅助文案只按当前语言显示", async () => {
  const scanner = new CameraScannerPortStub();
  mockLanguage = "zh";
  const chinese = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible
    />,
  );
  expect(chinese.getByText("商品条码")).toBeTruthy();
  expect(chinese.queryByText("Product barcode")).toBeNull();
  expect(chinese.queryByText("商品条码 / Product barcode")).toBeNull();
  await chinese.unmount();

  mockLanguage = "en";
  const english = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={new CameraScannerPortStub()}
      visible
    />,
  );
  expect(english.getByText("Product barcode")).toBeTruthy();
  expect(english.queryByText("商品条码")).toBeNull();
  expect(english.queryByText("商品条码 / Product barcode")).toBeNull();
  await english.unmount();
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
