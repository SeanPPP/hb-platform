import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { act, fireEvent, render } from "@testing-library/react-native";
import { AccessibilityInfo, StyleSheet } from "react-native";

import {
  SalesToolbar,
  type SalesToolbarAction,
} from "./sales-toolbar";
import { DEFAULT_SALES_TOOLBAR_ORDER } from "./sales-toolbar-order";

function action(
  id: SalesToolbarAction["id"],
  onPress = jest.fn(),
): SalesToolbarAction {
  return {
    id,
    label: id,
    onPress,
    testID: `toolbar-${id}`,
    tone: "quiet",
  };
}

async function layout(
  element: Awaited<ReturnType<typeof render>>["getByTestId"] extends (
    testId: string,
  ) => infer Element
    ? Element
    : never,
  x: number,
) {
  await fireEvent(element, "layout", {
    nativeEvent: { layout: { height: 44, width: 80, x, y: 0 } },
  });
}

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

describe("SalesToolbar", () => {
  it("工具栏始终保持单行并允许横向滚动", async () => {
    const screen = await render(
      <SalesToolbar
        actions={[
          action("held-orders"),
          action("daily-close"),
          action("returns"),
        ]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={jest.fn()}
      />,
    );
    const toolbar = screen.getByTestId("sales-toolbar");

    expect(toolbar.props.horizontal).toBe(true);
    expect(toolbar.props.scrollEnabled).toBe(true);
    expect(StyleSheet.flatten(toolbar.props.contentContainerStyle)).toMatchObject(
      {
        flexDirection: "row",
        flexWrap: "nowrap",
      },
    );
    expect(screen.queryByTestId("sales-toolbar-hidden-left")).toBeNull();
    expect(screen.queryByTestId("sales-toolbar-hidden-right")).toBeNull();

    await act(() =>
      toolbar.props.onLayout({
        nativeEvent: { layout: { height: 44, width: 320, x: 0, y: 0 } },
      }),
    );
    await act(() => toolbar.props.onContentSizeChange(640, 44));
    expect(screen.queryByTestId("sales-toolbar-hidden-left")).toBeNull();
    expect(screen.getByTestId("sales-toolbar-hidden-right")).toBeTruthy();

    await act(() =>
      toolbar.props.onScroll({
        nativeEvent: {
          contentOffset: { x: 120, y: 0 },
          contentSize: { height: 44, width: 640 },
          layoutMeasurement: { height: 44, width: 320 },
        },
      }),
    );
    expect(screen.getByTestId("sales-toolbar-hidden-left")).toBeTruthy();
    expect(screen.getByTestId("sales-toolbar-hidden-right")).toBeTruthy();

    await act(() =>
      toolbar.props.onScroll({
        nativeEvent: {
          contentOffset: { x: 320, y: 0 },
          contentSize: { height: 44, width: 640 },
          layoutMeasurement: { height: 44, width: 320 },
        },
      }),
    );
    expect(screen.getByTestId("sales-toolbar-hidden-left")).toBeTruthy();
    expect(screen.queryByTestId("sales-toolbar-hidden-right")).toBeNull();
    await screen.unmount();
  });

  it("短按会执行业务操作，disabled 仅阻止业务点击", async () => {
    const enabledPress = jest.fn();
    const disabledPress = jest.fn();
    const screen = await render(
      <SalesToolbar
        actions={[
          action("held-orders", enabledPress),
          { ...action("daily-close", disabledPress), disabled: true },
        ]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={jest.fn()}
      />,
    );

    await fireEvent.press(screen.getByTestId("toolbar-held-orders"));
    await fireEvent.press(screen.getByTestId("toolbar-daily-close"));

    expect(enabledPress).toHaveBeenCalledTimes(1);
    expect(disabledPress).not.toHaveBeenCalled();
    await screen.unmount();
  });

  it("长按前的轻微移动不吞掉仍有效的短按", async () => {
    const onPress = jest.fn();
    const screen = await render(
      <SalesToolbar
        actions={[action("held-orders", onPress)]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={jest.fn()}
      />,
    );
    const heldOrders = screen.getByTestId("toolbar-held-orders");
    await layout(screen.getByTestId("toolbar-held-orders-layout"), 0);

    await fireEvent(heldOrders, "responderGrant", {
      persist() {},
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await fireEvent(heldOrders, "responderMove", {
      nativeEvent: { pageX: 19, pageY: 10 },
    });
    expect(screen.getByTestId("sales-toolbar").props.scrollEnabled).toBe(true);
    await fireEvent(heldOrders, "touchEnd");
    await fireEvent.press(heldOrders);

    expect(onPress).toHaveBeenCalledTimes(1);
    await screen.unmount();
  });

  it("真实 responder 长按拖动只在松手后提交一次，并在拖动期间锁定滚动", async () => {
    const heldOrdersPress = jest.fn();
    const onOrderChange = jest.fn();
    const screen = await render(
      <SalesToolbar
        actions={[
          action("held-orders", heldOrdersPress),
          action("daily-close"),
        ]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={onOrderChange}
      />,
    );
    const heldOrders = screen.getByTestId("toolbar-held-orders");
    await layout(screen.getByTestId("toolbar-held-orders-layout"), 0);
    await layout(screen.getByTestId("toolbar-daily-close-layout"), 100);

    jest.useFakeTimers();
    await fireEvent(heldOrders, "responderGrant", {
      persist() {},
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await act(() => {
      jest.advanceTimersByTime(400);
    });

    expect(screen.getByTestId("sales-toolbar").props.scrollEnabled).toBe(false);
    expect(
      screen
        .getByTestId("toolbar-held-orders")
        .props.onStartShouldSetResponder.testOnly_pressabilityConfig()
        .cancelable,
    ).toBe(false);

    await fireEvent(heldOrders, "responderMove", {
      nativeEvent: { pageX: 110, pageY: 10 },
    });
    await fireEvent(heldOrders, "responderMove", {
      nativeEvent: { pageX: 110, pageY: 10 },
    });
    expect(onOrderChange).not.toHaveBeenCalled();
    await fireEvent(heldOrders, "touchEnd");
    await fireEvent(heldOrders, "press");

    expect(heldOrdersPress).not.toHaveBeenCalled();
    expect(onOrderChange).toHaveBeenCalledTimes(1);
    expect(onOrderChange).toHaveBeenLastCalledWith([
      "daily-close",
      "held-orders",
      ...DEFAULT_SALES_TOOLBAR_ORDER.slice(2),
    ]);
    expect(screen.getByTestId("sales-toolbar").props.scrollEnabled).toBe(true);
    await screen.unmount();
  });

  it("拖动收到 touch cancel 时回滚且不持久化半途顺序", async () => {
    const onOrderChange = jest.fn();
    const screen = await render(
      <SalesToolbar
        actions={[action("held-orders"), action("daily-close")]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={onOrderChange}
      />,
    );
    const heldOrders = screen.getByTestId("toolbar-held-orders");
    await layout(screen.getByTestId("toolbar-held-orders-layout"), 0);
    await layout(screen.getByTestId("toolbar-daily-close-layout"), 100);

    jest.useFakeTimers();
    await fireEvent(heldOrders, "responderGrant", {
      persist() {},
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await act(() => {
      jest.advanceTimersByTime(400);
    });
    await fireEvent(heldOrders, "responderMove", {
      nativeEvent: { pageX: 110, pageY: 10 },
    });
    await fireEvent(heldOrders, "touchCancel");

    expect(onOrderChange).not.toHaveBeenCalled();
    expect(screen.getByTestId("sales-toolbar").props.scrollEnabled).toBe(true);
    await screen.unmount();
  });

  it("无移动的长按不会写入顺序", async () => {
    const onOrderChange = jest.fn();
    const screen = await render(
      <SalesToolbar
        actions={[action("held-orders"), action("daily-close")]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={onOrderChange}
      />,
    );
    const heldOrders = screen.getByTestId("toolbar-held-orders");
    await layout(screen.getByTestId("toolbar-held-orders-layout"), 0);

    await fireEvent(heldOrders, "pressIn", {
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await fireEvent(heldOrders, "longPress");
    await fireEvent(
      screen.getByTestId("toolbar-held-orders-layout"),
      "touchEnd",
    );

    expect(onOrderChange).not.toHaveBeenCalled();
    await screen.unmount();
  });

  it("VoiceOver 前移/后移可排序，disabled 操作也仍可移动", async () => {
    const onOrderChange = jest.fn();
    const announce = jest
      .spyOn(AccessibilityInfo, "announceForAccessibility")
      .mockImplementation(() => undefined);
    const screen = await render(
      <SalesToolbar
        actions={[
          action("held-orders"),
          { ...action("daily-close"), disabled: true },
          action("returns"),
        ]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={onOrderChange}
      />,
    );
    expect(
      screen.getByTestId("toolbar-held-orders").props.accessibilityActions,
    ).toEqual([{ label: "Move later", name: "move-later" }]);

    await fireEvent(
      screen.getByTestId("toolbar-daily-close"),
      "accessibilityAction",
      { nativeEvent: { actionName: "move-later" } },
    );

    expect(onOrderChange).toHaveBeenCalledWith([
      "held-orders",
      "returns",
      "daily-close",
      ...DEFAULT_SALES_TOOLBAR_ORDER.slice(3),
    ]);
    expect(announce).toHaveBeenCalledWith(
      "daily-close moved to position 3 of 3.",
    );
    await screen.unmount();
  });
});
