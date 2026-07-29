import { beforeEach, expect, jest, test } from "@jest/globals";
import {
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import Storage from "expo-sqlite/kv-store";

import { DeviceRegistrationScreen } from "./device-registration-screen";

import type { DeviceRegistrationStore } from "@/core/api/hbpos-api";
import type { DeviceSessionState } from "@/core/security/device-session";
import i18n from "@/i18n";

jest.mock("expo-sqlite/kv-store", () => ({
  __esModule: true,
  default: {
    getItemSync: jest.fn(),
    setItem: jest.fn(),
  },
}));

const mockGetItemSync = jest.mocked(Storage.getItemSync);
const mockSetItem = jest.mocked(Storage.setItem);

const mockRegister = jest.fn<
  (input: Readonly<{ storeCode: string }>) => Promise<DeviceSessionState>
>();
const mockListRegistrationStores = jest.fn<
  () => Promise<readonly DeviceRegistrationStore[]>
>();
const mockUpdateOperationalState = jest.fn();
const mockRetry = jest.fn();
let mockRuntimeValue: unknown;

jest.mock("@expo/vector-icons", () => ({
  MaterialCommunityIcons: () => null,
}));

jest.mock("expo-router", () => ({
  Redirect: () => null,
}));

jest.mock("expo-localization", () => ({
  getLocales: () => [{ languageCode: "zh" }],
}));

jest.mock("expo-status-bar", () => ({
  StatusBar: () => null,
}));

jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: () => null,
}));

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntimeValue,
}));

beforeEach(async () => {
  jest.clearAllMocks();
  mockGetItemSync.mockReturnValue(null);
  mockSetItem.mockResolvedValue(undefined);
  mockListRegistrationStores.mockResolvedValue([
    { storeCode: "1002", storeName: "Aspley" },
    { storeCode: "1003", storeName: "Chermside" },
  ]);
  mockRegister.mockResolvedValue({
    status: "pending-approval",
    deviceCode: "POS_1003_1210",
    storeCode: "1003",
  });
  mockRuntimeValue = {
    retry: mockRetry,
    services: {
      deviceSession: {
        getDeviceIdentity: jest.fn(),
        listRegistrationStores: mockListRegistrationStores,
        poll: jest.fn(),
        register: mockRegister,
        reregister: jest.fn(),
      },
    },
    state: {
      backend: "reachable",
      database: "ready",
      device: "registration-required",
      phase: "registration-required",
    },
    updateOperationalState: mockUpdateOperationalState,
  };
  await i18n.changeLanguage("zh");
});

test("必须选择申请分店，提交时不发送终端名称", async () => {
  const screen = await render(<DeviceRegistrationScreen />);

  await waitFor(() =>
    expect(mockListRegistrationStores).toHaveBeenCalledTimes(1),
  );
  expect(screen.queryByText("终端名称")).toBeNull();
  expect(
    screen.getByTestId("registration-submit").props.accessibilityState.disabled,
  ).toBe(true);

  await fireEvent.press(screen.getByTestId("registration-store-picker"));
  await fireEvent.press(await screen.findByTestId("registration-store-1003"));
  await waitFor(() =>
    expect(screen.getByText("Chermside · 1003")).toBeTruthy(),
  );

  await fireEvent.press(screen.getByTestId("registration-submit"));

  await waitFor(() =>
    expect(mockRegister).toHaveBeenCalledWith({ storeCode: "1003" }),
  );
  expect(mockRegister.mock.calls[0]?.[0]).not.toHaveProperty("terminalName");
});

test("中英文切换保留已经选择的申请分店", async () => {
  const screen = await render(<DeviceRegistrationScreen />);
  await waitFor(() =>
    expect(mockListRegistrationStores).toHaveBeenCalledTimes(1),
  );

  await fireEvent.press(screen.getByTestId("registration-store-picker"));
  await fireEvent.press(await screen.findByTestId("registration-store-1002"));
  await fireEvent.press(screen.getByTestId("registration-language-switch"));

  await waitFor(() =>
    expect(screen.getByText("Request registration")).toBeTruthy(),
  );
  await waitFor(() =>
    expect(mockSetItem).toHaveBeenCalledWith("hb.pos.language.v1", "en"),
  );
  expect(screen.getByText("Aspley · 1002")).toBeTruthy();
  expect(
    screen.getByTestId("registration-submit").props.accessibilityState.disabled,
  ).toBe(false);
});
