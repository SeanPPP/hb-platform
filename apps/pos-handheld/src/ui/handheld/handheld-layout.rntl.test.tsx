import { expect, jest, test } from "@jest/globals";
import { render } from "@testing-library/react-native";
import { StyleSheet, Text } from "react-native";

import {
  HandheldPageHeader,
  HandheldScreenFrame,
  HandheldSection,
  HandheldStatusBadge,
} from "./handheld-layout";

jest.mock("expo-status-bar", () => ({ StatusBar: () => null }));

test("handheld screen keeps content and fixed action in separate regions", async () => {
  const screen = await render(
    <HandheldScreenFrame
      footer={<Text>确认收款</Text>}
      testID="payment-frame"
    >
      <Text>付款内容</Text>
    </HandheldScreenFrame>,
  );

  expect(screen.getByTestId("payment-frame-content")).toHaveTextContent(
    "付款内容",
  );
  expect(screen.getByTestId("payment-frame-footer")).toHaveTextContent(
    "确认收款",
  );
});

test("handheld header and section use compact production hierarchy without a logo", async () => {
  const screen = await render(
    <>
      <HandheldPageHeader
        eyebrow="门店 1003 · 收银员 018"
        subtitle="网络在线 · 待同步 0"
        title="收银"
      />
      <HandheldSection title="购物车">
        <Text>商品 3 件</Text>
      </HandheldSection>
    </>,
  );

  expect(screen.getByText("收银")).toBeTruthy();
  expect(screen.getByText("购物车")).toBeTruthy();
  expect(screen.queryByText("HB")).toBeNull();
});

test("handheld status badge exposes semantic state and compact height", async () => {
  const screen = await render(
    <HandheldStatusBadge label="已同步" tone="success" />,
  );
  const badge = screen.getByTestId("handheld-status-badge");

  expect(badge).toHaveAccessibilityValue({ text: "已同步" });
  expect(StyleSheet.flatten(badge.props.style).minHeight).toBe(24);
});
