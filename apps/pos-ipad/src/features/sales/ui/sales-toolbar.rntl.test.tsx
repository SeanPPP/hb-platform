import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { act, fireEvent, render } from "@testing-library/react-native";
import { Pressable } from "react-native";

import { DEFAULT_SALES_TOOLBAR_ORDER } from "./sales-toolbar-order";
import {
  SalesToolbar,
  type SalesToolbarAction,
} from "./sales-toolbar";

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
  jest.restoreAllMocks();
});

describe("SalesToolbar", () => {
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

  it("长按并拖动只在松手后提交一次排序，且不会误触业务操作", async () => {
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
    const dailyClose = screen.getByTestId("toolbar-daily-close");
    await layout(heldOrders, 0);
    await layout(dailyClose, 100);

    await fireEvent(heldOrders, "pressIn", {
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await fireEvent(heldOrders, "longPress");
    await fireEvent(heldOrders, "touchMove", {
      nativeEvent: { pageX: 110, pageY: 10 },
    });
    expect(onOrderChange).not.toHaveBeenCalled();
    await fireEvent(heldOrders, "pressOut");
    await fireEvent(heldOrders, "press");

    expect(heldOrdersPress).not.toHaveBeenCalled();
    expect(onOrderChange).toHaveBeenCalledTimes(1);
    expect(onOrderChange).toHaveBeenLastCalledWith([
      "daily-close",
      "held-orders",
      ...DEFAULT_SALES_TOOLBAR_ORDER.slice(2),
    ]);
    await screen.unmount();
  });

  it("拖动在 responder 被终止时同样只提交一次排序", async () => {
    const onOrderChange = jest.fn();
    const screen = await render(
      <SalesToolbar
        actions={[action("held-orders"), action("daily-close")]}
        canonicalOrder={DEFAULT_SALES_TOOLBAR_ORDER}
        onOrderChange={onOrderChange}
      />,
    );
    const heldOrders = screen.getByTestId("toolbar-held-orders");
    const dailyClose = screen.getByTestId("toolbar-daily-close");
    await layout(heldOrders, 0);
    await layout(dailyClose, 100);

    await fireEvent(heldOrders, "pressIn", {
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await fireEvent(heldOrders, "longPress");
    await fireEvent(heldOrders, "touchMove", {
      nativeEvent: { pageX: 110, pageY: 10 },
    });
    const heldOrdersPressable = screen.UNSAFE_getAllByType(Pressable)[0];
    if (!heldOrdersPressable) throw new Error("expected held-order Pressable");
    await act(() => heldOrdersPressable.props.onResponderTerminate?.({}));
    await act(() => heldOrdersPressable.props.onPressOut?.({}));

    expect(onOrderChange).toHaveBeenCalledTimes(1);
    expect(onOrderChange).toHaveBeenLastCalledWith([
      "daily-close",
      "held-orders",
      ...DEFAULT_SALES_TOOLBAR_ORDER.slice(2),
    ]);
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
    await layout(heldOrders, 0);

    await fireEvent(heldOrders, "pressIn", {
      nativeEvent: { pageX: 10, pageY: 10 },
    });
    await fireEvent(heldOrders, "longPress");
    await fireEvent(heldOrders, "pressOut");

    expect(onOrderChange).not.toHaveBeenCalled();
    await screen.unmount();
  });

  it("VoiceOver 前移/后移可排序，disabled 操作也仍可移动", async () => {
    const onOrderChange = jest.fn();
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
    await screen.unmount();
  });
});
