import { describe, expect, it, jest } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
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

describe("PosKeyboardAwareTextInput 扫码自动提交", () => {
  // 注意：自动提交的 180ms 定时器触发后，react-test-renderer 的后续 render 会失效，
  // 因此"触发提交"的场景必须放在本 describe 的最后，避免污染后续测试。
  it("手动输入节奏（字符间隔大）不触发自动提交", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="manual-idle-input"
      />,
    );

    const input = screen.getByTestId("manual-idle-input");
    await fireEvent.changeText(input, "9");
    // 手动输入间隔大于 60ms，不视为扫码节奏
    await new Promise((resolve) => setTimeout(resolve, 150));
    await fireEvent.changeText(input, "98");
    await new Promise((resolve) => setTimeout(resolve, 500));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("未启用扫码自动提交时不触发", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="plain-idle-input"
      />,
    );

    const input = screen.getByTestId("plain-idle-input");
    await fireEvent.changeText(input, "1");
    await fireEvent.changeText(input, "12");
    await new Promise((resolve) => setTimeout(resolve, 250));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("失焦后重聚焦不延续上次扫码节奏计数", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="refocus-input"
      />,
    );

    const input = screen.getByTestId("refocus-input");
    // 第一次聚焦：快速连发 3 次（不足 4 字符不触发）
    await fireEvent.changeText(input, "1");
    await fireEvent.changeText(input, "12");
    await fireEvent.changeText(input, "123");
    await fireEvent(input, "blur");
    // 重聚焦后再次快速连发：节奏计数已重置，不会延续上次计数触发提交
    await fireEvent.changeText(input, "4");
    await fireEvent.changeText(input, "45");
    await fireEvent.changeText(input, "456");
    await new Promise((resolve) => setTimeout(resolve, 250));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("手动回车提交会取消挂起的扫码自动提交，避免双提交", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="manual-submit-input"
      />,
    );

    const input = screen.getByTestId("manual-submit-input");
    // 快速连发 4 次，挂起 180ms 自动提交定时器
    await fireEvent.changeText(input, "9");
    await fireEvent.changeText(input, "98");
    await fireEvent.changeText(input, "987");
    await fireEvent.changeText(input, "9876");
    // 扫码器带回车：回车先触发手动提交，应取消挂起的自动提交
    await fireEvent(input, "submitEditing");
    // 等待超过 180ms，确认不会二次提交
    await new Promise((resolve) => setTimeout(resolve, 250));

    expect(onSubmitEditing).toHaveBeenCalledTimes(1);
  });

  it("扫码节奏（字符快速连发）停顿后自动触发提交，等效回车", async () => {
    const onSubmitEditing = jest.fn();
    const onChangeText = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={onChangeText}
        onSubmitEditing={onSubmitEditing}
        testID="scan-idle-input"
      />,
    );

    const input = screen.getByTestId("scan-idle-input");
    // 扫码器连发字符（同步触发，真实间隔远小于 60ms），连续 4 次满足节奏判定
    await fireEvent.changeText(input, "9");
    await fireEvent.changeText(input, "98");
    await fireEvent.changeText(input, "987");
    await fireEvent.changeText(input, "9876");
    // 等待 180ms 空闲定时器触发自动提交（waitFor 在 act 内轮询）
    await waitFor(() => {
      expect(onSubmitEditing).toHaveBeenCalledTimes(1);
    });

    expect(onChangeText).toHaveBeenCalledTimes(4);
  });

  it("慢速 HID 注入（逐字符间隔大于快速阈值但小于中速阈值）停顿后自动提交", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        mediumSpeedHidSubmit
        testID="slow-hid-input"
      />,
    );

    const input = screen.getByTestId("slow-hid-input");
    // 蓝牙 HID 扫码设备逐字符注入：字符间隔约 100ms，大于快速节奏阈值 60ms、
    // 小于中速阈值 250ms，需走慢速 HID 分支（不依赖 onKeyPress）。
    const chars = ["2", "29", "294", "2947", "29478", "294785", "2947858"];
    for (const value of chars) {
      await fireEvent.changeText(input, value);
      // 模拟慢速注入的字符间隔（>60ms 快速阈值，<250ms 中速阈值）
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    // 停止输入超过 300ms 后自动提交（等效回车）
    await waitFor(() => {
      expect(onSubmitEditing).toHaveBeenCalledTimes(1);
    });
  });

  it("整串注入（一次变更 ≥2 字符）长度足够时停顿后自动提交", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        mediumSpeedHidSubmit
        testID="bulk-inject-input"
      />,
    );

    const input = screen.getByTestId("bulk-inject-input");
    // DataWedge 等整串注入：一次 onChangeText 带完整条码（跳变 ≥2 字符）
    await fireEvent.changeText(input, "2947858456543");
    // 停止输入超过 300ms 后自动提交（等效回车）
    await waitFor(() => {
      expect(onSubmitEditing).toHaveBeenCalledTimes(1);
    });
  });

  it("手动慢速输入（间隔大于中速阈值且逐字符）即使长度足够也不自动提交", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="manual-slow-input"
      />,
    );

    const input = screen.getByTestId("manual-slow-input");
    // 手动软键盘输入：逐字符（每次 +1，无跳变）且间隔约 400ms（大于中速阈值 250ms），
    // 即使达到 HID_MIN_CHARS 长度并停顿，也不应自动提交。
    const chars = ["2", "29", "294", "2947", "29478", "294785", "2947858", "29478584"];
    for (const value of chars) {
      await fireEvent.changeText(input, value);
      await new Promise((resolve) => setTimeout(resolve, 400));
    }
    await new Promise((resolve) => setTimeout(resolve, 600));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("受控 value 下失焦重聚焦后续输不误判为整串注入", async () => {
    const onSubmitEditing = jest.fn();
    // 受控输入：父组件 value 固定为已输入内容（模拟失焦后显示值仍在）。
    const controlledValue = "294785";
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="controlled-refocus-input"
        value={controlledValue}
      />,
    );

    const input = screen.getByTestId("controlled-refocus-input");
    // 失焦（组件内部重置计数），重聚焦后用户续输 1 个字符：
    // 基准应为受控 value（6 字符），增量 1 → 不判定整串注入。
    await fireEvent(input, "blur");
    await fireEvent.changeText(input, "2947858");
    await new Promise((resolve) => setTimeout(resolve, 600));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("未启用 mediumSpeedHidSubmit 时慢速/整串注入不自动提交（销售/退货页面无回归）", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="medium-disabled-input"
      />,
    );

    const input = screen.getByTestId("medium-disabled-input");
    // 销售/退货等页面仅启用 autoSubmitOnScanIdle：慢速逐字符注入（100ms 间隔）
    // 与整串注入都不应触发自动提交，保持原快速节奏行为。
    const chars = ["2", "29", "294", "2947", "29478", "294785", "2947858"];
    for (const value of chars) {
      await fireEvent.changeText(input, value);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    await fireEvent.changeText(input, "2947858456543");
    await new Promise((resolve) => setTimeout(resolve, 600));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("挂起自动提交定时器后失焦不提交（防后台弹窗误触发）", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        mediumSpeedHidSubmit
        testID="blur-cancel-input"
      />,
    );

    const input = screen.getByTestId("blur-cancel-input");
    // 中速 HID 注入已挂起 300ms 自动提交定时器
    const chars = ["2", "29", "294", "2947", "29478", "294785", "2947858"];
    for (const value of chars) {
      await fireEvent.changeText(input, value);
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    // 定时器触发前失焦：挂起的自动提交必须被取消
    await fireEvent(input, "blur");
    await new Promise((resolve) => setTimeout(resolve, 500));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });

  it("受控 value 被外部清空（清除按钮）时取消挂起的自动提交", async () => {
    const onSubmitEditing = jest.fn();
    const screen = await render(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        mediumSpeedHidSubmit
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="external-clear-input"
        value="294785"
      />,
    );

    // 中速注入挂起 300ms 自动提交定时器
    await fireEvent.changeText(
      screen.getByTestId("external-clear-input"),
      "2947858",
    );
    // 外部清空（如清除按钮 setBarcode("")）：受控 value 变为空应取消挂起定时器
    await screen.rerender(
      <PosKeyboardAwareTextInput
        autoSubmitOnScanIdle
        mediumSpeedHidSubmit
        onChangeText={() => undefined}
        onSubmitEditing={onSubmitEditing}
        testID="external-clear-input"
        value=""
      />,
    );
    await new Promise((resolve) => setTimeout(resolve, 500));

    expect(onSubmitEditing).not.toHaveBeenCalled();
  });
});
