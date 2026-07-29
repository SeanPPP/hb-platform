/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { render } from "@testing-library/react-native";

jest.doMock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.doMock("expo-camera", () => {
  const React = require("react");
  const { View } = require("react-native");
  const CameraView = (props: Record<string, unknown>) => React.createElement(View, props);
  CameraView.isAvailableAsync = jest.fn();
  return { CameraView, useCameraPermissions: jest.fn(() => [{ granted: false, status: "denied" }, jest.fn()]) };
});

const { CameraScannerModal } = require("./camera-scanner-modal") as typeof import("./camera-scanner-modal");
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

test("已拒绝权限时提示设置路径且不初始化相机", async () => {
  const scanner = new CameraScannerPortStub();
  const rendered = await render(
    <CameraScannerModal
      context="supervisor-authorization"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible
    />,
  );

  expect(rendered.getByTestId("camera-scanner-permission-denied")).toBeTruthy();
  expect(rendered.queryByTestId("camera-scanner-preview")).toBeNull();
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
