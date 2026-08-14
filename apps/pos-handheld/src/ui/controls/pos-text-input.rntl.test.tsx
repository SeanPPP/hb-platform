import { createRef } from "react";
import { beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { TextInput } from "react-native";

import { usePosSound } from "@/ui/feedback/pos-sound-context";

import { PosTextInput } from "./pos-text-input";

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

test("真实触控结束才发出输入音，且完整保留 TextInput 属性", async () => {
  const onChangeText = jest.fn();
  const screen = await render(
    <PosTextInput
      accessibilityLabel="Search"
      onChangeText={onChangeText}
      placeholder="Search product"
      testID="search"
      value=""
    />,
  );

  const input = screen.getByTestId("search");
  expect(input.props.placeholder).toBe("Search product");
  await fireEvent(input, "touchStart");
  await fireEvent(input, "touchEnd");
  await fireEvent.changeText(input, "123");

  expect(play).toHaveBeenCalledWith("key");
  expect(onChangeText).toHaveBeenCalledWith("123");
});

test("程序 focus、value 改变、HID 文本与滑动均不发声", async () => {
  const screen = await render(<PosTextInput testID="search" value="" />);
  const input = screen.getByTestId("search");

  await fireEvent(input, "focus");
  await fireEvent.changeText(input, "HID-001");
  await fireEvent(input, "touchStart");
  await fireEvent(input, "touchMove");
  await fireEvent(input, "touchEnd");

  expect(play).not.toHaveBeenCalled();
});

test("不可编辑输入框不发声", async () => {
  const screen = await render(
    <PosTextInput editable={false} testID="disabled-search" value="" />,
  );

  const input = screen.getByTestId("disabled-search");
  await fireEvent(input, "touchStart");
  await fireEvent(input, "touchEnd");

  expect(play).not.toHaveBeenCalled();
});

test("完整转发 TextInput ref，程序 focus 不触发触控音", async () => {
  const ref = createRef<TextInput>();
  await render(<PosTextInput ref={ref} testID="search" value="" />);

  expect(ref.current).not.toBeNull();
  const focus = jest.spyOn(ref.current!, "focus");
  ref.current!.focus();

  expect(focus).toHaveBeenCalledTimes(1);
  expect(play).not.toHaveBeenCalled();
});
