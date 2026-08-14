import { describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { createRef } from "react";
import { FlatList, ScrollView, TextInput } from "react-native";

import {
  PosKeyboardAwareFlatList,
  PosKeyboardAwareScrollView,
  PosKeyboardAwareTextInput,
} from "./pos-keyboard-aware-scroll-view";

describe("PosKeyboardAwareScrollView", () => {
  it("固定系统键盘滚动策略并完整转发 ScrollView 属性与 ref", async () => {
    const ref = createRef<ScrollView>();
    const screen = await render(
      <PosKeyboardAwareScrollView
        accessibilityLabel="Keyboard aware form"
        automaticallyAdjustKeyboardInsets={false}
        contentContainerStyle={{ padding: 24 }}
        keyboardDismissMode="none"
        keyboardShouldPersistTaps="always"
        ref={ref}
        testID="keyboard-aware-scroll"
      />,
    );

    const scroll = screen.getByTestId("keyboard-aware-scroll");
    expect(scroll.props.accessibilityLabel).toBe("Keyboard aware form");
    expect(scroll.props.contentContainerStyle).toEqual({ padding: 24 });
    expect(scroll.props.automaticallyAdjustKeyboardInsets).toBe(true);
    expect(scroll.props.keyboardDismissMode).toBe("interactive");
    expect(scroll.props.keyboardShouldPersistTaps).toBe("handled");
    expect(ref.current).not.toBeNull();
  });

  it("系统键盘输入聚焦时按默认间距滚到键盘上方并组合调用 onFocus", async () => {
    const revealFocusedInput = jest.spyOn(
      ScrollView.prototype,
      "scrollResponderScrollNativeHandleToKeyboard",
    );
    const onFocus = jest.fn();
    const screen = await render(
      <PosKeyboardAwareScrollView testID="keyboard-aware-scroll">
        <PosKeyboardAwareTextInput
          accessibilityHint="Type a value"
          onFocus={onFocus}
          placeholder="Value"
          testID="keyboard-aware-input"
        />
      </PosKeyboardAwareScrollView>,
    );
    const focusEvent = { target: 202 };

    await fireEvent(
      screen.getByTestId("keyboard-aware-input"),
      "focus",
      focusEvent,
    );

    expect(revealFocusedInput).toHaveBeenCalledTimes(1);
    expect(revealFocusedInput).toHaveBeenCalledWith(202, 16, true);
    expect(onFocus).toHaveBeenCalledTimes(1);
    expect(onFocus).toHaveBeenCalledWith(focusEvent);
    expect(
      screen.getByTestId("keyboard-aware-input").props.accessibilityHint,
    ).toBe("Type a value");
    expect(screen.getByTestId("keyboard-aware-input").props.placeholder).toBe(
      "Value",
    );

    revealFocusedInput.mockRestore();
  });

  it("允许页面覆盖焦点滚动间距", async () => {
    const revealFocusedInput = jest.spyOn(
      ScrollView.prototype,
      "scrollResponderScrollNativeHandleToKeyboard",
    );
    const screen = await render(
      <PosKeyboardAwareScrollView keyboardRevealOffset={32}>
        <PosKeyboardAwareTextInput testID="custom-offset-input" />
      </PosKeyboardAwareScrollView>,
    );

    await fireEvent(screen.getByTestId("custom-offset-input"), "focus", {
      target: 303,
    });

    expect(revealFocusedInput).toHaveBeenCalledWith(303, 32, true);
    revealFocusedInput.mockRestore();
  });

  it("禁用系统键盘时不主动滚动，但仍转发输入属性、ref 与 onFocus", async () => {
    const revealFocusedInput = jest.spyOn(
      ScrollView.prototype,
      "scrollResponderScrollNativeHandleToKeyboard",
    );
    const ref = createRef<TextInput>();
    const onChangeText = jest.fn();
    const onFocus = jest.fn();
    const screen = await render(
      <PosKeyboardAwareScrollView>
        <PosKeyboardAwareTextInput
          onChangeText={onChangeText}
          onFocus={onFocus}
          ref={ref}
          showSoftInputOnFocus={false}
          testID="hid-safe-input"
          value=""
        />
      </PosKeyboardAwareScrollView>,
    );
    const input = screen.getByTestId("hid-safe-input");
    const focusEvent = { target: 404 };

    await fireEvent(input, "focus", focusEvent);
    await fireEvent.changeText(input, "HID-001");

    expect(revealFocusedInput).not.toHaveBeenCalled();
    expect(onFocus).toHaveBeenCalledWith(focusEvent);
    expect(onChangeText).toHaveBeenCalledWith("HID-001");
    expect(input.props.showSoftInputOnFocus).toBe(false);
    expect(ref.current).not.toBeNull();

    revealFocusedInput.mockRestore();
  });

  it("已聚焦输入切换为系统键盘模式时主动揭示当前输入", async () => {
    const revealFocusedInput = jest.spyOn(
      ScrollView.prototype,
      "scrollResponderScrollNativeHandleToKeyboard",
    );
    const screen = await render(
      <PosKeyboardAwareScrollView>
        <PosKeyboardAwareTextInput
          showSoftInputOnFocus={false}
          testID="manual-keyboard-input"
        />
      </PosKeyboardAwareScrollView>,
    );

    await fireEvent(screen.getByTestId("manual-keyboard-input"), "focus", {
      target: 606,
    });
    expect(revealFocusedInput).not.toHaveBeenCalled();

    await screen.rerender(
      <PosKeyboardAwareScrollView>
        <PosKeyboardAwareTextInput
          showSoftInputOnFocus
          testID="manual-keyboard-input"
        />
      </PosKeyboardAwareScrollView>,
    );

    expect(revealFocusedInput).toHaveBeenCalledTimes(1);
    expect(revealFocusedInput).toHaveBeenCalledWith(606, 16, true);
    revealFocusedInput.mockRestore();
  });

  it("脱离 ScrollView provider 仍保留输入行为且不会崩溃", async () => {
    const ref = createRef<TextInput>();
    const onChangeText = jest.fn();
    const onFocus = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        onChangeText={onChangeText}
        onFocus={onFocus}
        ref={ref}
        testID="standalone-input"
        value=""
      />,
    );
    const input = screen.getByTestId("standalone-input");
    const focusEvent = { target: 505 };

    await expect(
      fireEvent(input, "focus", focusEvent),
    ).resolves.toBeUndefined();
    await fireEvent.changeText(input, "Standalone");

    expect(onFocus).toHaveBeenCalledWith(focusEvent);
    expect(onChangeText).toHaveBeenCalledWith("Standalone");
    expect(ref.current).not.toBeNull();
  });
});

describe("PosKeyboardAwareFlatList", () => {
  it("固定键盘策略并把 FlatList 属性、ref 转发到底层滚动组件", async () => {
    const ref = createRef<FlatList<{ id: string }>>();
    const screen = await render(
      <PosKeyboardAwareFlatList
        accessibilityLabel="Keyboard aware list"
        automaticallyAdjustKeyboardInsets={false}
        contentContainerStyle={{ padding: 24 }}
        data={[{ id: "1" }]}
        keyboardDismissMode="none"
        keyboardShouldPersistTaps="always"
        keyExtractor={(item) => item.id}
        ref={ref}
        renderItem={({ item }) => (
          <PosKeyboardAwareTextInput testID={`input-${item.id}`} />
        )}
        testID="keyboard-aware-list"
      />,
    );

    const scroll = screen.getByTestId("keyboard-aware-list");
    expect(scroll.props.accessibilityLabel).toBe("Keyboard aware list");
    expect(scroll.props.contentContainerStyle).toEqual({ padding: 24 });
    expect(scroll.props.automaticallyAdjustKeyboardInsets).toBe(true);
    expect(scroll.props.keyboardDismissMode).toBe("interactive");
    expect(scroll.props.keyboardShouldPersistTaps).toBe("handled");
    expect(screen.getByTestId("input-1")).toBeTruthy();
    expect(ref.current).not.toBeNull();
    expect(typeof ref.current?.scrollToOffset).toBe("function");
  });

  it("自定义 keyboardRevealOffset，并在列表内输入聚焦时按该间距揭示", async () => {
    const revealFocusedInput = jest.spyOn(
      ScrollView.prototype,
      "scrollResponderScrollNativeHandleToKeyboard",
    );
    const screen = await render(
      <PosKeyboardAwareFlatList
        data={[{ id: "1" }]}
        keyboardRevealOffset={48}
        keyExtractor={(item) => item.id}
        renderItem={() => <PosKeyboardAwareTextInput testID="list-input" />}
      />,
    );

    await fireEvent(screen.getByTestId("list-input"), "focus", {
      target: 707,
    });

    expect(revealFocusedInput).toHaveBeenCalledTimes(1);
    expect(revealFocusedInput).toHaveBeenCalledWith(707, 48, true);
    revealFocusedInput.mockRestore();
  });

  it("列表内 HID 输入聚焦保持静默，不触发揭示滚动", async () => {
    const revealFocusedInput = jest.spyOn(
      ScrollView.prototype,
      "scrollResponderScrollNativeHandleToKeyboard",
    );
    const screen = await render(
      <PosKeyboardAwareFlatList
        data={[{ id: "1" }]}
        keyExtractor={(item) => item.id}
        renderItem={() => (
          <PosKeyboardAwareTextInput
            showSoftInputOnFocus={false}
            testID="hid-list-input"
          />
        )}
      />,
    );

    await fireEvent(screen.getByTestId("hid-list-input"), "focus", {
      target: 808,
    });

    expect(revealFocusedInput).not.toHaveBeenCalled();
    expect(screen.getByTestId("hid-list-input").props.showSoftInputOnFocus).toBe(
      false,
    );
    revealFocusedInput.mockRestore();
  });
});
