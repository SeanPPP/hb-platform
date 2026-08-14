import { expect, test } from "@jest/globals";
import { render } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import {
  LocalHistoryReceiptPreview,
  type LocalHistoryReceiptDocument,
} from "./local-history-receipt-preview";

const document = {
  paper: "58mm",
  lines: [
    { kind: "text", text: "HOT BARGAIN", align: "center", bold: true },
    {
      kind: "separator",
      text: "--------------------------------",
      align: "left",
      bold: false,
    },
    { kind: "barcode", value: "HB-ORDER-000042" },
    { kind: "qr", value: "10000000-0000-4000-8000-000000000042" },
  ],
} as const satisfies LocalHistoryReceiptDocument;

test("小票预览使用窄白纸、等宽文字并完整保留条码和二维码载荷", async () => {
  const screen = await render(
    <LocalHistoryReceiptPreview document={document} />,
  );

  const paper = screen.getByTestId("local-history-receipt-paper");
  expect(StyleSheet.flatten(paper.props.style)).toEqual(
    expect.objectContaining({
      alignSelf: "center",
      backgroundColor: "#FFFFFF",
      maxWidth: 312,
    }),
  );
  expect(
    StyleSheet.flatten(
      screen.getByTestId("local-history-receipt-line-0").props.style,
    ).fontFamily,
  ).toBe("Menlo");
  expect(screen.getByText("--------------------------------")).toBeTruthy();
  expect(
    screen.getByTestId("local-history-receipt-barcode-2").props
      .accessibilityLabel,
  ).toContain("HB-ORDER-000042");
  expect(
    screen.getAllByTestId(/local-history-receipt-barcode-run-2-/u).length,
  ).toBeGreaterThan(20);
  expect(
    screen.getByTestId("local-history-receipt-qr-3").props
      .accessibilityLabel,
  ).toContain("10000000-0000-4000-8000-000000000042");
  expect(
    screen.getAllByTestId(/local-history-receipt-qr-cell-3-/u).length,
  ).toBeGreaterThan(100);
  const qrRows = screen.getAllByTestId(
    /local-history-receipt-qr-row-3-/u,
  );
  expect(qrRows.length).toBeGreaterThan(20);
  expect(qrRows[0]?.children).toHaveLength(qrRows.length);
});
