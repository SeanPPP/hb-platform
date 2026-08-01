import { beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { createRef } from "react";
import { TextInput } from "react-native";

import { PosTextInput } from "./pos-text-input";

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

test("真实轻触结束播放 key 音并保留输入属性", async () => {
  const onChangeText = jest.fn();
  const screen = await render(
    <PosTextInput
      onChangeText={onChangeText}
      placeholder="Search"
      testID="input"
    />,
  );
  const input = screen.getByTestId("input");

  await fireEvent(input, "touchStart");
  await fireEvent(input, "touchEnd");
  await fireEvent.changeText(input, "123");

  expect(input.props.placeholder).toBe("Search");
  expect(play).toHaveBeenCalledWith("key");
  expect(onChangeText).toHaveBeenCalledWith("123");
});

test("程序 focus、HID 文本、滑动与不可编辑输入均静音", async () => {
  const screen = await render(
    <>
      <PosTextInput testID="input" />
      <PosTextInput editable={false} testID="disabled" />
    </>,
  );
  const input = screen.getByTestId("input");

  await fireEvent(input, "focus");
  await fireEvent.changeText(input, "HID-1");
  await fireEvent(input, "touchStart");
  await fireEvent(input, "touchMove");
  await fireEvent(input, "touchEnd");
  await fireEvent(screen.getByTestId("disabled"), "touchStart");
  await fireEvent(screen.getByTestId("disabled"), "touchEnd");

  expect(play).not.toHaveBeenCalled();
});

test("完整转发原生 TextInput ref", async () => {
  const ref = createRef<TextInput>();
  await render(<PosTextInput ref={ref} testID="ref" />);
  expect(ref.current).not.toBeNull();
});
