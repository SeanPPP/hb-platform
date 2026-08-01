import { expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";

import { SalesNumberKeypad } from "./sales-number-keypad";

import { PosSoundContext } from "@/ui/feedback/pos-sound-context";

test("数字键使用按键音，清除键使用危险操作音", async () => {
  const onKeyPress = jest.fn();
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
      <SalesNumberKeypad
        labels={{
          backspace: "Backspace",
          clear: "Clear",
          decimal: "Decimal",
          quick50: "Quick 50",
          quick99: "Quick 99",
        }}
        mode="decimal"
        onKeyPress={onKeyPress}
        testIDPrefix="amount"
      />
    </PosSoundContext.Provider>,
  );

  for (const key of ["1", "backspace", "decimal", "clear"] as const) {
    await fireEvent.press(screen.getByTestId(`amount-key-${key}`));
  }

  expect(play.mock.calls.map(([cue]) => cue)).toEqual([
    "key",
    "key",
    "key",
    "danger",
  ]);
  expect(onKeyPress.mock.calls.map(([key]) => key)).toEqual([
    "1",
    "backspace",
    "decimal",
    "clear",
  ]);
});
