/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";

jest.doMock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

const requestPermission = jest.fn();

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

test("未决定权限时由收银员主动请求，不初始化相机", async () => {
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
