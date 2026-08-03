import { beforeEach, expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { createRef } from "react";
import { Text, type View } from "react-native";

import { PosPressable } from "./pos-pressable";

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

test("默认 tap 音先于业务 onPress 发出，并完整保留 Pressable 属性", async () => {
  const onPress = jest.fn();
  const screen = await render(
    <PosPressable
      accessibilityHint="Add item"
      accessibilityLabel="Add"
      onPress={onPress}
      testID="add"
    >
      <Text>Add</Text>
    </PosPressable>,
  );

  const target = screen.getByTestId("add");
  expect(target.props.accessibilityHint).toBe("Add item");
  await fireEvent.press(target);

  expect(play).toHaveBeenCalledWith("tap");
  expect(onPress).toHaveBeenCalledTimes(1);
  const playOrder = play.mock.invocationCallOrder[0];
  const pressOrder = onPress.mock.invocationCallOrder[0];
  expect(playOrder).toBeDefined();
  expect(pressOrder).toBeDefined();
  if (playOrder === undefined || pressOrder === undefined) {
    throw new Error("触控音与业务回调均应被调用");
  }
  expect(playOrder).toBeLessThan(pressOrder);
});

test("普通按钮不注册长按处理，长按释放仍按普通 tap 与 onPress 执行", async () => {
  const onPress = jest.fn();
  const screen = await render(
    <PosPressable onPress={onPress} testID="ordinary">
      <Text>Ordinary</Text>
    </PosPressable>,
  );

  const target = screen.getByTestId("ordinary");
  expect(target.props.onLongPress).toBeUndefined();
  await fireEvent(target, "longPress");
  await fireEvent.press(target);

  expect(play).toHaveBeenCalledTimes(1);
  expect(play).toHaveBeenCalledWith("tap");
  expect(onPress).toHaveBeenCalledTimes(1);
});

test("null 长按回调或 false 长按音均不注册长按，并保持普通 tap", async () => {
  const nullOnLongPressPress = jest.fn();
  const falseLongPressSoundPress = jest.fn();
  const screen = await render(
    <>
      <PosPressable
        onLongPress={null}
        onPress={nullOnLongPressPress}
        testID="null-long-press"
      >
        <Text>Null long press</Text>
      </PosPressable>
      <PosPressable
        longPressSound={false}
        onPress={falseLongPressSoundPress}
        testID="false-long-press-sound"
      >
        <Text>False long press sound</Text>
      </PosPressable>
    </>,
  );

  for (const testID of ["null-long-press", "false-long-press-sound"]) {
    const target = screen.getByTestId(testID);
    await fireEvent(target, "longPress");
    await fireEvent.press(target);
  }

  expect(play).toHaveBeenCalledTimes(2);
  expect(play).toHaveBeenLastCalledWith("tap");
  expect(nullOnLongPressPress).toHaveBeenCalledTimes(1);
  expect(falseLongPressSoundPress).toHaveBeenCalledTimes(1);
});

test("完整转发 Pressable 的 View ref", async () => {
  const ref = createRef<View>();
  await render(
    <PosPressable ref={ref} testID="ref-target">
      <Text>Ref</Text>
    </PosPressable>,
  );

  expect(ref.current).not.toBeNull();
});

test("禁用、取消与滚动手势均不发音", async () => {
  const screen = await render(
    <>
      <PosPressable disabled testID="disabled">
        <Text>Disabled</Text>
      </PosPressable>
      <PosPressable testID="cancelled">
        <Text>Cancelled</Text>
      </PosPressable>
      <PosPressable testID="scrolling">
        <Text>Scrolling</Text>
      </PosPressable>
    </>,
  );

  await fireEvent.press(screen.getByTestId("disabled"));
  await fireEvent(screen.getByTestId("cancelled"), "pressIn");
  await fireEvent(screen.getByTestId("cancelled"), "pressOut");
  await fireEvent(screen.getByTestId("scrolling"), "pressIn");
  await fireEvent(screen.getByTestId("scrolling"), "pressOut");

  expect(play).not.toHaveBeenCalled();
});

test("长按发出指定音且后续 press 不重复短按音", async () => {
  const onPress = jest.fn();
  const onLongPress = jest.fn();
  const screen = await render(
    <PosPressable
      longPressSound="danger"
      onLongPress={onLongPress}
      onPress={onPress}
      sound="key"
      testID="hold"
    >
      <Text>Hold</Text>
    </PosPressable>,
  );

  const target = screen.getByTestId("hold");
  await fireEvent(target, "pressIn");
  await fireEvent(target, "longPress");
  await fireEvent.press(target);

  expect(play).toHaveBeenCalledTimes(1);
  expect(play).toHaveBeenCalledWith("danger");
  expect(onLongPress).toHaveBeenCalledTimes(1);
  expect(onPress).toHaveBeenCalledTimes(1);
});
