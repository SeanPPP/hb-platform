import { beforeEach, expect, jest, test } from "@jest/globals";
import {
  act,
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import Storage from "expo-sqlite/kv-store";

import { DeviceRegistrationScreen } from "./device-registration-screen";

import type { DeviceActivationPreviewResponse } from "@/core/api/hbpos-api";
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

const activationCode =
  "HBDEV1-0123456789ABCDEFGHJKMNPQRS-STVWXYZ0123456789ABCDEFGHJ";
const mockPreviewActivationCode = jest.fn<
  (value: string) => Promise<DeviceActivationPreviewResponse>
>();
const mockRedeemActivationCode = jest.fn<
  (input: Readonly<{ activationCode: string }>) => Promise<DeviceSessionState>
>();
const mockRestorePendingActivationCode = jest.fn<() => Promise<string | null>>();
const mockClearPendingActivationCode = jest.fn<() => Promise<void>>();
const mockUpdateOperationalState = jest.fn();
const mockRetry = jest.fn<() => Promise<void>>();
const mockServerTest = jest.fn<
  (address: string, signal: AbortSignal) => Promise<boolean>
>();
const mockServerChange = jest.fn<
  (
    address: string,
    signal: AbortSignal,
  ) => Promise<
    | Readonly<{ status: "completed"; apiBaseUrl: string }>
    | Readonly<{
        status: "blocked";
        reason: "pending-local-data" | "candidate-unreachable";
      }>
  >
>();
let mockRuntimeValue: unknown;
let mockLatestCameraProps: Readonly<{
  context: string;
  onScan(value: string): void;
  visible: boolean;
}> | null = null;

jest.mock("@expo/vector-icons", () => ({
  MaterialCommunityIcons: () => null,
}));

jest.mock("@/core/api/hbpos-api", () => ({
  ...jest.requireActual<object>("@/core/api/hbpos-api"),
  resolveHbposDeviceSystem: () => "Android",
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

jest.mock("@/features/scanner-camera/camera-scanner-modal", () => ({
  CameraScannerModal: (props: typeof mockLatestCameraProps) => {
    mockLatestCameraProps = props;
    return null;
  },
}));

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => mockRuntimeValue,
}));

beforeEach(async () => {
  jest.clearAllMocks();
  mockGetItemSync.mockReturnValue(null);
  mockSetItem.mockResolvedValue(undefined);
  mockPreviewActivationCode.mockResolvedValue({
    isAllowed: true,
    storeCode: "1042",
    storeName: "Sunnybank",
    deviceSystem: "Android",
    expiresAtUtc: "2026-08-27T12:00:00.000Z",
  });
  mockRedeemActivationCode.mockResolvedValue({
    status: "authorized",
    deviceCode: "ANDROID-1042-01",
    storeCode: "1042",
  });
  mockRestorePendingActivationCode.mockResolvedValue(null);
  mockClearPendingActivationCode.mockResolvedValue(undefined);
  mockRetry.mockResolvedValue(undefined);
  mockServerTest.mockResolvedValue(true);
  mockServerChange.mockResolvedValue({
    status: "completed",
    apiBaseUrl: "https://hotbargain.vip/pos-api",
  });
  mockRuntimeValue = {
    retry: mockRetry,
    services: {
      deviceSession: {
        clearPendingActivationCode: mockClearPendingActivationCode,
        getDeviceIdentity: jest.fn(),
        poll: jest.fn(),
        previewActivationCode: mockPreviewActivationCode,
        redeemActivationCode: mockRedeemActivationCode,
        register: jest.fn(),
        restorePendingActivationCode: mockRestorePendingActivationCode,
        reregister: jest.fn(),
      },
      scanner: {
        router: {
          acceptCameraText: jest.fn(() => true),
          acquireContext: jest.fn(() => jest.fn()),
          startCamera: jest.fn(async () => undefined),
          stopCamera: jest.fn(async () => undefined),
        },
      },
      serverConnection: {
        change: mockServerChange,
        getCurrentApiBaseUrl: () => "https://hotbargain.vip/pos-api",
        test: mockServerTest,
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
  mockLatestCameraProps = null;
});

test("待开通码读取损坏时显示恢复错误且不把它当作空记录", async () => {
  mockRestorePendingActivationCode.mockRejectedValue(
    new Error("Device activation recovery data is unavailable."),
  );

  const screen = await render(<DeviceRegistrationScreen />);

  expect(
    await screen.findByText("Device activation recovery data is unavailable."),
  ).toBeTruthy();
});

test("编辑输入框不得清除上一次结果不确定的待恢复开通码", async () => {
  mockRestorePendingActivationCode.mockResolvedValue(activationCode);
  const screen = await render(<DeviceRegistrationScreen />);
  const input = screen.getByTestId("registration-activation-code");
  await waitFor(() => expect(input.props.value).toBe(activationCode));

  await fireEvent.changeText(input, `${activationCode}X`);

  expect(mockClearPendingActivationCode).not.toHaveBeenCalled();
});

test("手动输入先预览服务端权威分店，确认后才兑换", async () => {
  const screen = await render(<DeviceRegistrationScreen />);

  expect(screen.getByTestId("handheld-state-device-registration")).toBeTruthy();
  expect(screen.queryByText("HB")).toBeNull();
  expect(
    screen.queryByText(/iPad|customer display|external display|客显/iu),
  ).toBeNull();
  await fireEvent.changeText(
    screen.getByTestId("registration-activation-code"),
    activationCode.toLowerCase(),
  );
  await fireEvent.press(screen.getByTestId("registration-preview"));

  await waitFor(() =>
    expect(mockPreviewActivationCode).toHaveBeenCalledWith(activationCode),
  );
  expect(screen.getByText("Sunnybank · 1042")).toBeTruthy();
  expect(screen.getByText("设备平台：Android")).toBeTruthy();
  expect(screen.getByText(/2026/)).toBeTruthy();
  expect(mockRedeemActivationCode).not.toHaveBeenCalled();

  await fireEvent.press(screen.getByTestId("registration-redeem"));
  await waitFor(() =>
    expect(mockRedeemActivationCode).toHaveBeenCalledWith({ activationCode }),
  );
});

test.each([
  ["pending-approval", "pending"],
  ["authorized", "approved"],
  ["denied", "rejected"],
  ["disabled", "disabled"],
] as const)(
  "提交后的 %s 由真实注册结果进入 %s 状态",
  async (status, visibleState) => {
    mockRedeemActivationCode.mockResolvedValueOnce({
      status,
      deviceCode: "POS_1003_1210",
      storeCode: "1003",
      ...(status === "denied" || status === "disabled"
        ? { message: `registration.${visibleState}` }
        : {}),
    });
    const screen = await render(<DeviceRegistrationScreen />);
    await fireEvent.changeText(
      screen.getByTestId("registration-activation-code"),
      activationCode,
    );
    await fireEvent.press(screen.getByTestId("registration-preview"));
    await screen.findByText("Sunnybank · 1042");
    await fireEvent.press(screen.getByTestId("registration-redeem"));

    expect(
      await screen.findByTestId("handheld-state-registration-states"),
    ).toBeTruthy();
    expect(
      await screen.findByTestId(`registration-state-${visibleState}`),
    ).toBeTruthy();
  },
);

test("扫码使用私有上下文且中英文切换保留开通码", async () => {
  const screen = await render(<DeviceRegistrationScreen />);

  await fireEvent.press(screen.getByTestId("registration-scan"));
  expect(mockLatestCameraProps?.context).toBe("device-activation");
  await act(async () => {
    mockLatestCameraProps?.onScan(activationCode.toLowerCase());
  });
  await waitFor(() =>
    expect(mockPreviewActivationCode).toHaveBeenCalledWith(activationCode),
  );
  await fireEvent.press(screen.getByTestId("registration-language-switch"));

  await waitFor(() =>
    expect(screen.getByText("Activate device")).toBeTruthy(),
  );
  await waitFor(() =>
    expect(mockSetItem).toHaveBeenCalledWith("hb.pos.language.v1", "en"),
  );
  expect(screen.getByTestId("registration-activation-code").props.value).toBe(
    activationCode,
  );
});

test("未注册设备测试通过后可安全切换服务器并重建 runtime", async () => {
  const screen = await render(<DeviceRegistrationScreen />);
  await fireEvent.press(screen.getByTestId("server-connection-edit"));
  await fireEvent.changeText(
    screen.getByTestId("server-connection-input"),
    "https://hotbargain.top/pos-api",
  );
  await fireEvent.press(screen.getByTestId("server-connection-test"));

  await waitFor(() =>
    expect(mockServerTest).toHaveBeenCalledWith(
      "https://hotbargain.top/pos-api",
      expect.any(AbortSignal),
    ),
  );
  await fireEvent.press(screen.getByTestId("server-connection-save"));
  await fireEvent.press(screen.getByTestId("server-connection-confirm"));

  await waitFor(() =>
    expect(mockServerChange).toHaveBeenCalledWith(
      "https://hotbargain.top/pos-api",
      expect.any(AbortSignal),
    ),
  );
  await waitFor(() => expect(mockRetry).toHaveBeenCalledTimes(1));
});

test("locked 状态只显示恢复提示并禁止扫码、手输和服务器修改", async () => {
  mockRuntimeValue = {
    ...(mockRuntimeValue as Record<string, unknown>),
    state: {
      backend: "offline",
      database: "ready",
      device: "locked",
      phase: "locked",
    },
  };

  const screen = await render(<DeviceRegistrationScreen />);

  expect(screen.getByTestId("registration-recovery-readonly")).toBeTruthy();
  expect(screen.queryByTestId("registration-scan")).toBeNull();
  expect(screen.queryByTestId("registration-activation-code")).toBeNull();
  expect(screen.queryByTestId("registration-preview")).toBeNull();
  expect(screen.queryByTestId("server-connection-edit")).toBeNull();
});
