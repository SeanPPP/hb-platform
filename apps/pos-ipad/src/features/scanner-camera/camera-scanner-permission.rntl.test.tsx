/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { beforeEach, expect, jest, test } from "@jest/globals";
import { act, fireEvent, render, waitFor } from "@testing-library/react-native";

jest.doMock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

const requestPermission = jest.fn<() => Promise<void>>();

jest.doMock("expo-camera", () => {
  const React = require("react");
  const { View } = require("react-native");
  const CameraView = (props: Record<string, unknown>) => React.createElement(View, props);
  CameraView.isAvailableAsync = jest.fn();
  return {
    CameraView,
    useCameraPermissions: jest.fn(() => [
      { granted: false, status: "undetermined" },
      requestPermission,
    ]),
  };
});

const { CameraScannerModal } = require("./camera-scanner-modal") as typeof import("./camera-scanner-modal");
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

beforeEach(() => {
  requestPermission.mockReset();
});

test("未决定权限时由收银员主动请求，不初始化相机", async () => {
  requestPermission.mockResolvedValue(undefined);
  const scanner = new CameraScannerPortStub();
  const rendered = await render(
    <CameraScannerModal
      context="cashier-login"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible
    />,
  );

  fireEvent.press(rendered.getByTestId("camera-scanner-request-permission"));
  expect(requestPermission).toHaveBeenCalledTimes(1);
  expect(scanner.startCalls).toBe(0);
});

test("权限请求异常时进入不可用状态且不产生未处理拒绝", async () => {
  requestPermission.mockRejectedValue(new Error("permission unavailable"));
  const rendered = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={new CameraScannerPortStub()}
      visible
    />,
  );

  await act(async () => {
    fireEvent.press(rendered.getByTestId("camera-scanner-request-permission"));
    await Promise.resolve();
  });

  await waitFor(() =>
    expect(rendered.getByTestId("camera-scanner-unavailable")).toBeTruthy(),
  );
});

test("权限请求同步抛错时安全进入不可用状态", async () => {
  requestPermission.mockImplementation(() => {
    throw new Error("permission bridge unavailable");
  });
  const rendered = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={new CameraScannerPortStub()}
      visible
    />,
  );

  fireEvent.press(rendered.getByTestId("camera-scanner-request-permission"));

  await waitFor(() =>
    expect(rendered.getByTestId("camera-scanner-unavailable")).toBeTruthy(),
  );
});

test("权限请求在途时忽略重复点击", async () => {
  requestPermission.mockImplementation(
    () => new Promise<void>(() => undefined),
  );
  const rendered = await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={new CameraScannerPortStub()}
      visible
    />,
  );
  const button = rendered.getByTestId("camera-scanner-request-permission");

  await fireEvent.press(button);
  await fireEvent.press(button);

  expect(requestPermission).toHaveBeenCalledTimes(1);
});

class CameraScannerPortStub implements CameraScannerPort {
  public startCalls = 0;

  public acceptCameraText(): boolean {
    return true;
  }

  public async startCamera(): Promise<void> {
    this.startCalls += 1;
  }

  public async stopCamera(): Promise<void> {}
}
