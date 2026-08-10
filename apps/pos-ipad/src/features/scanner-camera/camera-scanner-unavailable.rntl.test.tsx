/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";

jest.doMock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

jest.doMock("expo-camera", () => {
  const React = require("react");
  const { View } = require("react-native");
  const CameraView = (props: Record<string, unknown>) =>
    React.createElement(View, { ...props, testID: "camera-scanner-preview" });
  CameraView.isAvailableAsync = jest
    .fn<() => Promise<boolean>>()
    .mockRejectedValue(new Error("isAvailableAsync is unavailable on iOS"));
  return { CameraView, useCameraPermissions: jest.fn(() => [{ granted: true, status: "granted" }, jest.fn()]) };
});

const { CameraScannerModal } = require("./camera-scanner-modal") as typeof import("./camera-scanner-modal");
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

test("iOS 不支持 Web 能力探测时仍挂载相机，并在原生挂载失败后 fail-closed", async () => {
  const scanner = new CameraScannerPortStub();
  const rendered = await render(
    <CameraScannerModal
      context="product-search"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible
    />,
  );

  const preview = await rendered.findByTestId("camera-scanner-preview");
  await waitFor(() => expect(scanner.startCalls).toBe(1));

  await fireEvent(preview, "onMountError", new Error("native camera unavailable"));

  await rendered.findByTestId("camera-scanner-unavailable");
  expect(rendered.queryByTestId("camera-scanner-preview")).toBeNull();
  await waitFor(() => expect(scanner.stopCalls).toBe(1));
});

class CameraScannerPortStub implements CameraScannerPort {
  public startCalls = 0;
  public stopCalls = 0;

  public acceptCameraText(): boolean {
    return true;
  }

  public async startCamera(): Promise<void> {
    this.startCalls += 1;
  }

  public async stopCamera(): Promise<void> {
    this.stopCalls += 1;
  }
}
