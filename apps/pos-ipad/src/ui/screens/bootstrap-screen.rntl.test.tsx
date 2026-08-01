import { expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";

import { BootstrapScreen } from "./bootstrap-screen";

import { PosSoundContext } from "@/ui/feedback/pos-sound-context";

const mockRetry = jest.fn(async () => undefined);

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
      device: "unknown",
      error: "runtime failed",
      phase: "failed",
    },
  }),
}));
jest.mock("@/ui/shell/pos-shell-store", () => ({
  usePosShellStore: (selector: (state: { display: string }) => unknown) =>
    selector({ display: "unavailable" }),
}));
jest.mock("@/ui/shell/status-strip", () => ({
  PosStatusStrip: () => null,
}));

test("运行时重试按钮先播放普通按钮音，再调用重试", async () => {
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

  await fireEvent.press(screen.getByTestId("bootstrap-retry"));

  expect(play).toHaveBeenCalledWith("tap");
  expect(mockRetry).toHaveBeenCalledTimes(1);
  expect(play.mock.invocationCallOrder[0]).toBeLessThan(
    mockRetry.mock.invocationCallOrder[0] ?? Number.POSITIVE_INFINITY,
  );
});
