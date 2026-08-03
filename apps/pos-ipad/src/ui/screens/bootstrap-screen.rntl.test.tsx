import { beforeEach, describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";

import { BootstrapScreen } from "./bootstrap-screen";

import { PosSoundContext } from "@/ui/feedback/pos-sound-context";

const mockRetry = jest.fn<() => Promise<void>>();
const mockServerTest = jest.fn<
  (address: string, signal: AbortSignal) => Promise<boolean>
>();

jest.mock("@expo/vector-icons", () => ({
  MaterialCommunityIcons: () => null,
}));

jest.mock("expo-status-bar", () => ({ StatusBar: () => null }));

jest.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

jest.mock("@/core/runtime/pos-runtime-context", () => ({
  usePosRuntime: () => ({
    retry: mockRetry,
    state: {
      backend: "unreachable",
      database: "failed",
      device: "unauthorized",
      error: "bootstrap.error",
      phase: "failed",
    },
  }),
}));

jest.mock("@/core/runtime/expo-bootstrap-server-diagnostics", () => ({
  loadExpoBootstrapServerDiagnostics: () =>
    Promise.resolve({
      currentApiBaseUrl: "https://hotbargain.vip/pos-api",
      test: mockServerTest,
    }),
}));

jest.mock("@/ui/shell/pos-shell-store", () => ({
  usePosShellStore: (selector: (state: { display: string }) => unknown) =>
    selector({ display: "ready" }),
}));

jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: () => null,
}));

describe("BootstrapScreen", () => {
  beforeEach(() => {
    mockRetry.mockReset();
    mockRetry.mockResolvedValue(undefined);
    mockServerTest.mockReset();
    mockServerTest.mockResolvedValue(true);
  });

  it("失败时重试保留原有调用，并发出 tap 触控音", async () => {
    const play = jest.fn();
    const screen = await render(
      <PosSoundContext.Provider
        value={{
          buttonSoundEnabled: true,
          play,
          setButtonSoundEnabled: jest.fn(),
          setSpecialNodeSoundEnabled: jest.fn(),
          specialNodeSoundEnabled: true,
        }}
      >
        <BootstrapScreen />
      </PosSoundContext.Provider>,
    );

    await fireEvent.press(screen.getByText("bootstrap.retry"));

    expect(mockRetry).toHaveBeenCalledTimes(1);
    expect(play).toHaveBeenCalledWith("tap");
    await screen.unmount();
  });

  it("失败准备页可修改候选地址并测试，但不允许绕过账本门禁保存", async () => {
    const screen = await render(<BootstrapScreen />);
    await waitFor(() =>
      expect(screen.getByTestId("server-connection-panel")).toBeTruthy(),
    );

    await fireEvent.press(screen.getByTestId("server-connection-edit"));
    await fireEvent.press(screen.getByTestId("server-connection-test"));

    await waitFor(() =>
      expect(mockServerTest).toHaveBeenCalledWith(
        "https://hotbargain.vip/pos-api",
        expect.any(AbortSignal),
      ),
    );
    expect(
      screen.getByTestId("server-connection-save").props.accessibilityState
        .disabled,
    ).toBe(true);
    expect(
      screen.getByTestId("server-connection-save-disabled-reason"),
    ).toBeTruthy();
  });
});
