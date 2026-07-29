/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";

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
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

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
