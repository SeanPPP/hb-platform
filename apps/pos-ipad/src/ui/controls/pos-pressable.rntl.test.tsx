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

test("默认 tap 音先于业务 onPress，并完整保留属性", async () => {
  const onPress = jest.fn();
  const screen = await render(
    <PosPressable accessibilityHint="Add item" onPress={onPress} testID="add">
      <Text>Add</Text>
    </PosPressable>,
  );
  const target = screen.getByTestId("add");

  await fireEvent.press(target);

  expect(target.props.accessibilityHint).toBe("Add item");
  expect(play).toHaveBeenCalledWith("tap");
  expect(onPress).toHaveBeenCalledTimes(1);
  expect(play.mock.invocationCallOrder[0]).toBeLessThan(
    onPress.mock.invocationCallOrder[0]!,
  );
});

test("未传 disabled 时不向原生无障碍状态注入 false", async () => {
  const screen = await render(
    <PosPressable
      accessibilityRole="checkbox"
      accessibilityState={{ checked: true }}
      testID="checked"
    />,
  );

  expect(screen.getByTestId("checked").props.accessibilityState).toEqual({
    checked: true,
  });
});

test("禁用或显式静音时不发声", async () => {
  const onPress = jest.fn();
  const screen = await render(
    <>
      <PosPressable disabled onPress={onPress} testID="disabled" />
      <PosPressable onPress={onPress} sound={false} testID="silent" />
    </>,
  );

  await fireEvent.press(screen.getByTestId("disabled"));
  await fireEvent.press(screen.getByTestId("silent"));

  expect(play).not.toHaveBeenCalled();
  expect(onPress).toHaveBeenCalledTimes(1);
});

test("长按只发指定音，释放时不重复短按音", async () => {
  const onLongPress = jest.fn();
  const onPress = jest.fn();
  const screen = await render(
    <PosPressable
      longPressSound="navigate"
      onLongPress={onLongPress}
      onPress={onPress}
      sound="tap"
      testID="hold"
    />,
  );
  const target = screen.getByTestId("hold");

  await fireEvent(target, "pressIn");
  await fireEvent(target, "longPress");
  await fireEvent.press(target);

  expect(play).toHaveBeenCalledTimes(1);
  expect(play).toHaveBeenCalledWith("navigate");
  expect(onLongPress).toHaveBeenCalledTimes(1);
  expect(onPress).toHaveBeenCalledTimes(1);
});

test("完整转发原生 View ref", async () => {
  const ref = createRef<View>();
  await render(<PosPressable ref={ref} testID="ref" />);
  expect(ref.current).not.toBeNull();
});
