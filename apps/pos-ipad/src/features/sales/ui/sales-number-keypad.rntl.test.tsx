import { describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";

import {
  SalesNumberKeypad,
  type SalesNumberKey,
} from "./sales-number-keypad";

import { PosSoundContext } from "@/ui/feedback/pos-sound-context";

describe("SalesNumberKeypad", () => {
  it("数字、退格和小数使用按键音，清除使用危险操作音", async () => {
    const onKeyPress = jest.fn<(key: SalesNumberKey) => void>();
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
            quick50: "$0.50",
            quick99: "$0.99",
          }}
          mode="decimal"
          onKeyPress={onKeyPress}
          testIDPrefix="keypad"
        />
      </PosSoundContext.Provider>,
    );

    await fireEvent.press(screen.getByTestId("keypad-key-1"));
    await fireEvent.press(screen.getByTestId("keypad-key-backspace"));
    await fireEvent.press(screen.getByTestId("keypad-key-decimal"));
    await fireEvent.press(screen.getByTestId("keypad-key-clear"));

    expect(onKeyPress.mock.calls.map(([key]) => key)).toEqual([
      "1",
      "backspace",
      "decimal",
      "clear",
    ]);
    expect(play.mock.calls.map(([sound]) => sound)).toEqual([
      "key",
      "key",
      "key",
      "danger",
    ]);
    await screen.unmount();
  });
});
