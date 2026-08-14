import { expect, jest, test } from "@jest/globals";
import { fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

jest.mock("expo-status-bar", () => ({ StatusBar: () => null }));

import {
  HELD_ORDERS_MIN_TOUCH_TARGET,
  HeldOrdersScreen,
} from "./held-orders-screen";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({ i18n: { language: "en", resolvedLanguage: "en" } }),
}));

test("挂单列表以单列进入真实详情，48px 恢复操作仍调用原 Presenter", async () => {
  expect(HELD_ORDERS_MIN_TOUCH_TARGET).toBe(48);
  const state: any = {
    kind: "ready",
    busy: false,
    lastAction: null,
    rows: [
      {
        holdId: "recover-1",
        localSequence: 8,
        scope: { storeCode: "BNE", deviceCode: "IPAD-1" },
        heldBy: { cashierId: "C1", cashierName: "Cashier" },
        status: "Recalling",
        itemCount: 2,
        subtotalCents: 1_200,
        discountCents: 0,
        actualAmountCents: 1_200,
        heldAtIso: "2026-07-28T01:00:00.000Z",
        recallingAtIso: "2026-07-28T01:01:00.000Z",
      },
    ],
  };
  const listeners = new Set<() => void>();
  const presenter: any = {
    getState: () => state,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    refresh: jest.fn(async () => undefined),
    hold: jest.fn(async () => ({ ok: true, code: "held" })),
    recall: jest.fn(async () => ({ ok: true, code: "recalled" })),
    recover: jest.fn(async () => ({ ok: true, code: "recovered" })),
    release: jest.fn(async () => ({ ok: true, code: "released" })),
  };
  const screen = await render(<HeldOrdersScreen presenter={presenter} />);
  await waitFor(() => expect(presenter.refresh).toHaveBeenCalledTimes(1));
  expect(screen.getByTestId("handheld-state-held-orders-list")).toBeTruthy();
  expect(screen.getByTestId("held-order-row-recover-1")).toBeTruthy();
  expect(screen.queryByText("Product 1")).toBeNull();
  await fireEvent.press(screen.getByTestId("held-orders-hold"));
  expect(presenter.hold).toHaveBeenCalledTimes(1);
  await fireEvent.press(screen.getByTestId("held-order-view-recover-1"));
  expect(screen.queryByTestId("handheld-state-held-orders-list")).toBeNull();
  expect(screen.getByTestId("handheld-state-held-order-detail")).toBeTruthy();
  expect(
    StyleSheet.flatten(
      screen.getByTestId("handheld-state-held-order-detail").props.style,
    ).flexDirection,
  ).toBe("column");
  expect(
    StyleSheet.flatten(
      screen.getByTestId("held-order-action-recover-1").props.style,
    ).minHeight,
  ).toBeGreaterThanOrEqual(48);
  await fireEvent.press(screen.getByTestId("held-order-action-recover-1"));
  expect(presenter.recover).toHaveBeenCalledWith("recover-1");
  expect(
    screen.getByTestId("held-order-detail-back").props.accessibilityState
      .disabled,
  ).toBe(false);
});
