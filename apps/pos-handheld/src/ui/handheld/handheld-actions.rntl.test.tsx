import { expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import { HandheldActionButton } from "./handheld-actions";

import { PosSoundContext } from "@/ui/feedback/pos-sound-context";

test("handheld primary action is a 48px orange control and keeps touch feedback", async () => {
  const onPress = jest.fn();
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
      <HandheldActionButton
        label="确认收款"
        onPress={onPress}
        testID="confirm-payment"
      />
    </PosSoundContext.Provider>,
  );
  const button = screen.getByTestId("confirm-payment");
  const style = StyleSheet.flatten(button.props.style);

  expect(style.minHeight).toBe(48);
  expect(style.backgroundColor).toBe("#E65A2F");
  await fireEvent.press(button);
  expect(onPress).toHaveBeenCalledTimes(1);
  expect(play).toHaveBeenCalledWith("tap");
});

test("handheld disabled action exposes accessibility state", async () => {
  const screen = await render(
    <HandheldActionButton
      disabled
      label="正在处理"
      onPress={jest.fn()}
      testID="processing"
    />,
  );

  expect(screen.getByTestId("processing")).toBeDisabled();
});
