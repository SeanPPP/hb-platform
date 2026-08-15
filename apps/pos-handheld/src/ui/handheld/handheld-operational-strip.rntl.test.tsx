import { expect, jest, test } from "@jest/globals";
import { fireEvent, render } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import { HandheldOperationalStrip } from "./handheld-operational-strip";

test("operational strip carries the six mobile POS statuses and never adds a display item", async () => {
  const screen = await render(
    <HandheldOperationalStrip
      items={[
        { key: "store", label: "门店", value: "Sunnybank" },
        { key: "cashier", label: "员工", value: "018" },
        { key: "network", label: "网络", value: "在线", tone: "success" },
        { key: "sync", label: "同步", value: "0", tone: "success" },
        { key: "scanner", label: "扫码", value: "就绪", tone: "success" },
        { key: "printer", label: "打印机", value: "已连接", tone: "success" },
      ]}
    />,
  );

  expect(screen.getAllByTestId("handheld-operational-item")).toHaveLength(6);
  expect(screen.queryByText(/客显|display/iu)).toBeNull();
});

test("operational strip is a single-line horizontal scroll view without wrapping", async () => {
  const screen = await render(
    <HandheldOperationalStrip
      items={[
        { key: "store", label: "门店", value: "Sunnybank" },
        { key: "network", label: "网络", value: "在线", tone: "success" },
      ]}
    />,
  );

  const strip = screen.getByTestId("handheld-operational-strip");
  expect(strip).toHaveProp("horizontal", true);
  expect(StyleSheet.flatten(strip.props.style)).toMatchObject({
    flexGrow: 0,
    flexShrink: 0,
  });
  expect(StyleSheet.flatten(strip.props.style).flexWrap).toBeUndefined();
  expect(
    StyleSheet.flatten(strip.props.contentContainerStyle).flexWrap,
  ).toBeUndefined();
});

test("operational strip pressable items keep label semantics and fire callbacks", async () => {
  const onPress = jest.fn();
  const screen = await render(
    <HandheldOperationalStrip
      items={[
        {
          accessibilityHint: "查看门店状态",
          accessibilityLiveRegion: "polite",
          key: "store",
          label: "门店",
          onPress,
          value: "Sunnybank",
        },
        { key: "network", label: "网络", value: "在线", tone: "success" },
      ]}
    />,
  );

  const items = screen.getAllByTestId("handheld-operational-item");
  expect(items).toHaveLength(2);
  const pressableItem = items[0]!;
  const readonlyItem = items[1]!;

  expect(pressableItem).toHaveAccessibleName("门店: Sunnybank");
  expect(pressableItem.props.accessibilityRole).toBe("button");
  expect(pressableItem.props.accessibilityHint).toBe("查看门店状态");
  expect(pressableItem).toHaveProp("accessibilityLiveRegion", "polite");
  fireEvent.press(pressableItem);
  expect(onPress).toHaveBeenCalledTimes(1);

  expect(readonlyItem).toHaveAccessibleName("网络: 在线");
  expect(readonlyItem.props.accessibilityRole).toBeUndefined();
  expect(readonlyItem.props.onPress).toBeUndefined();
});

test("compact operational strip targets 48dp for container and items", async () => {
  const screen = await render(
    <HandheldOperationalStrip
      compact
      items={[
        { key: "store", label: "门店", value: "Sunnybank" },
        { key: "network", label: "网络", value: "在线", tone: "success" },
      ]}
    />,
  );

  const strip = screen.getByTestId("handheld-operational-strip");
  expect(StyleSheet.flatten(strip.props.style).minHeight).toBe(48);

  for (const item of screen.getAllByTestId("handheld-operational-item")) {
    expect(StyleSheet.flatten(item.props.style).minHeight).toBe(48);
  }
});

test("operational strip accepts a custom container style", async () => {
  const screen = await render(
    <HandheldOperationalStrip
      items={[{ key: "network", label: "网络", value: "在线" }]}
      style={{ backgroundColor: "rgb(255, 0, 0)" }}
    />,
  );

  expect(
    StyleSheet.flatten(
      screen.getByTestId("handheld-operational-strip").props.style,
    ).backgroundColor,
  ).toBe("rgb(255, 0, 0)");
});
