/* eslint-disable @typescript-eslint/no-require-imports -- Jest runtime mock must load before the component. */
import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import {
  act,
  cleanup,
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import { Platform } from "react-native";

jest.doMock("expo-status-bar", () => ({ StatusBar: () => null }));

jest.doMock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

let permission = { granted: true, status: "granted" };
const requestPermission = jest.fn<() => Promise<void>>();
const isAvailableAsync = jest.fn<() => Promise<boolean>>();

jest.doMock("expo-camera", () => {
  const React = require("react");
  const { View } = require("react-native");
  const CameraView = (props: Record<string, unknown>) =>
    React.createElement(View, { ...props, testID: "camera-scanner-preview" });
  CameraView.isAvailableAsync = isAvailableAsync;
  return {
    CameraView,
    useCameraPermissions: jest.fn(() => [permission, requestPermission]),
  };
});

const { CameraScannerModal } = require("./camera-scanner-modal") as typeof import("./camera-scanner-modal");
type CameraScannerPort = import("./camera-scanner-modal").CameraScannerPort;

const originalPlatform = Platform.OS;

function setPlatform(os: "android" | "ios" | "web") {
  Object.defineProperty(Platform, "OS", { configurable: true, value: os });
}

function createDeferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((complete) => {
    resolve = complete;
  });
  return { promise, resolve };
}

async function renderModal(scanner: CameraScannerPort, visible = true) {
  return await render(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible={visible}
    />,
  );
}

test.each(["ios", "android"] as const)(
  "%s 原生相机不调用仅 Web 支持的 isAvailableAsync",
  async (os) => {
    setPlatform(os);
    permission = { granted: true, status: "granted" };
    isAvailableAsync.mockReset().mockResolvedValue(false);

    const rendered = await renderModal(new CameraScannerPortStub());

    await rendered.findByTestId("camera-scanner-preview");
    expect(isAvailableAsync).not.toHaveBeenCalled();
    await rendered.unmount();
  },
);

test("关闭后重开时，旧 startCamera 迟到完成不能停止新会话", async () => {
  setPlatform("ios");
  permission = { granted: true, status: "granted" };
  const firstStart = createDeferred();
  const secondStart = createDeferred();
  const scanner = new CameraScannerPortStub();
  scanner.startCamera = jest
    .fn<() => Promise<void>>()
    .mockReturnValueOnce(firstStart.promise)
    .mockReturnValueOnce(secondStart.promise);
  const rendered = await renderModal(scanner);

  await waitFor(() => expect(scanner.startCamera).toHaveBeenCalledTimes(1));
  await rendered.rerender(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible={false}
    />,
  );
  await rendered.rerender(
    <CameraScannerModal
      context="product"
      onClose={jest.fn()}
      onScan={jest.fn()}
      scanner={scanner}
      visible
    />,
  );
  await waitFor(() => expect(scanner.startCamera).toHaveBeenCalledTimes(2));
  await act(async () => {
    secondStart.resolve();
    await secondStart.promise;
  });
  await rendered.findByTestId("camera-scanner-preview");
  const stopsBeforeOldStartCompletes = scanner.stopCamera.mock.calls.length;

  await act(async () => {
    firstStart.resolve();
    await firstStart.promise;
  });

  await waitFor(() =>
    expect(scanner.stopCamera).toHaveBeenCalledTimes(
      stopsBeforeOldStartCompletes,
    ),
  );
  await rendered.unmount();
});

test.each([
  ["同步抛错", () => {
    throw new Error("permission failed");
  }],
  ["Promise 拒绝", () => Promise.reject(new Error("permission failed"))],
] as const)("权限请求%s时稳定失败且不启动相机", async (_caseName, reject) => {
  setPlatform("ios");
  permission = { granted: false, status: "undetermined" };
  requestPermission.mockReset().mockImplementation(reject);
  const scanner = new CameraScannerPortStub();
  const rendered = await renderModal(scanner);

  await act(async () => {
    fireEvent.press(
      rendered.getByTestId("camera-scanner-request-permission"),
    );
    await Promise.resolve();
  });

  await rendered.findByTestId("camera-scanner-unavailable");
  expect(requestPermission).toHaveBeenCalledTimes(1);
  expect(scanner.startCamera).not.toHaveBeenCalled();
  await rendered.unmount();
});

afterEach(() => {
  cleanup();
  Object.defineProperty(Platform, "OS", {
    configurable: true,
    value: originalPlatform,
  });
  permission = { granted: true, status: "granted" };
  requestPermission.mockReset();
  isAvailableAsync.mockReset();
});

beforeEach(() => {
  isAvailableAsync.mockResolvedValue(true);
});

class CameraScannerPortStub implements CameraScannerPort {
  public acceptCameraText(): boolean {
    return true;
  }

  public startCamera = jest.fn<() => Promise<void>>().mockResolvedValue();

  public stopCamera = jest.fn<() => Promise<void>>().mockResolvedValue();
}
