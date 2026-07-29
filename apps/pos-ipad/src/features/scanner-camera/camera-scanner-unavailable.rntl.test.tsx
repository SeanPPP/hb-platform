/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { render } from "@testing-library/react-native";

jest.doMock("expo-camera", () => {
  const React = require("react");
  const { View } = require("react-native");
  const CameraView = (props: Record<string, unknown>) => React.createElement(View, props);
  CameraView.isAvailableAsync = jest
    .fn<() => Promise<boolean>>()
    .mockResolvedValue(false);
  return { CameraView, useCameraPermissions: jest.fn(() => [{ granted: true, status: "granted" }, jest.fn()]) };
});

const { CameraScannerModal } = require("./camera-scanner-modal") as typeof import("./camera-scanner-modal");
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

test("无原生相机能力时 fail-closed，不交付条码", async () => {
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

  await rendered.findByTestId("camera-scanner-unavailable");
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
