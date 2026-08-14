import { createRef } from "react";
import { beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { Switch } from "react-native";

import { usePosSound } from "@/ui/feedback/pos-sound-context";

import { PosSwitch } from "./pos-switch";

jest.mock("@/ui/feedback/pos-sound-context", () => ({
  usePosSound: jest.fn(),
}));

const mockUsePosSound = jest.mocked(usePosSound);
const play = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
});

test("开关在业务回调前同步播放默认 tap，并完整透传 Switch 属性", async () => {
  const callOrder: string[] = [];
  const onValueChange = jest.fn<(value: boolean) => void>((value) => {
    callOrder.push(`value:${value}`);
  });
  play.mockImplementation((cue) => callOrder.push(`sound:${cue}`));
  mockUsePosSound.mockReturnValue({
    buttonSoundEnabled: true,
    play,
    setButtonSoundEnabled: jest.fn(),
    setSpecialNodeSoundEnabled: jest.fn(),
    specialNodeSoundEnabled: true,
  });

  const screen = await render(
    <PosSwitch
      accessibilityLabel="触控音效"
      onValueChange={onValueChange}
      testID="touch-sound"
      trackColor={{ false: "#111111", true: "#222222" }}
      value
    />,
  );

  const control = screen.getByTestId("touch-sound");
  await fireEvent(control, "valueChange", false);

  expect(play).toHaveBeenCalledWith("tap");
  expect(onValueChange).toHaveBeenCalledWith(false);
  expect(callOrder).toEqual(["sound:tap", "value:false"]);
});

test("禁用或显式关音时不播放，但禁用状态不交给业务回调", async () => {
  const onValueChange = jest.fn<(value: boolean) => void>();
  mockUsePosSound.mockReturnValue({
    buttonSoundEnabled: true,
    play,
    setButtonSoundEnabled: jest.fn(),
    setSpecialNodeSoundEnabled: jest.fn(),
    specialNodeSoundEnabled: true,
  });

  const screen = await render(
    <>
      <PosSwitch
        disabled
        onValueChange={onValueChange}
        testID="disabled-switch"
        value
      />
      <PosSwitch
        onValueChange={onValueChange}
        sound={false}
        testID="silent-switch"
        value={false}
      />
    </>,
  );

  await fireEvent(screen.getByTestId("disabled-switch"), "valueChange", false);
  await fireEvent(screen.getByTestId("silent-switch"), "valueChange", true);

  expect(play).not.toHaveBeenCalled();
  expect(onValueChange).toHaveBeenCalledTimes(1);
  expect(onValueChange).toHaveBeenCalledWith(true);
});

test("完整转发原生 Switch ref", async () => {
  const ref = createRef<Switch>();
  mockUsePosSound.mockReturnValue({
    buttonSoundEnabled: true,
    play,
    setButtonSoundEnabled: jest.fn(),
    setSpecialNodeSoundEnabled: jest.fn(),
    specialNodeSoundEnabled: true,
  });

  await render(<PosSwitch ref={ref} testID="switch-ref" value={false} />);

  expect(ref.current).not.toBeNull();
});
