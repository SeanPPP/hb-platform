import { beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { createRef } from "react";
import { Switch } from "react-native";

import { PosSwitch } from "./pos-switch";

import { usePosSound } from "@/ui/feedback/pos-sound-context";

jest.mock("@/ui/feedback/pos-sound-context", () => ({ usePosSound: jest.fn() }));

const mockUsePosSound = jest.mocked(usePosSound);
const play = jest.fn();

beforeEach(() => {
  jest.clearAllMocks();
  mockUsePosSound.mockReturnValue({
    buttonSoundEnabled: true,
    play,
    setButtonSoundEnabled: jest.fn(),
    setSpecialNodeSoundEnabled: jest.fn(),
    specialNodeSoundEnabled: true,
  });
});

test("默认 tap 音先于业务 valueChange", async () => {
  const onValueChange = jest.fn<(value: boolean) => void>();
  const screen = await render(
    <PosSwitch onValueChange={onValueChange} testID="switch" value />,
  );

  await fireEvent(screen.getByTestId("switch"), "valueChange", false);

  expect(play).toHaveBeenCalledWith("tap");
  expect(onValueChange).toHaveBeenCalledWith(false);
  expect(play.mock.invocationCallOrder[0]).toBeLessThan(
    onValueChange.mock.invocationCallOrder[0]!,
  );
});

test("禁用与显式静音均不播放", async () => {
  const onValueChange = jest.fn<(value: boolean) => void>();
  const screen = await render(
    <>
      <PosSwitch disabled onValueChange={onValueChange} testID="disabled" />
      <PosSwitch onValueChange={onValueChange} sound={false} testID="silent" />
    </>,
  );

  await fireEvent(screen.getByTestId("disabled"), "valueChange", true);
  await fireEvent(screen.getByTestId("silent"), "valueChange", true);

  expect(play).not.toHaveBeenCalled();
  expect(onValueChange).toHaveBeenCalledTimes(1);
});

test("完整转发原生 Switch ref", async () => {
  const ref = createRef<Switch>();
  await render(<PosSwitch ref={ref} testID="ref" value={false} />);
  expect(ref.current).not.toBeNull();
});
