import { beforeEach, expect, jest, test } from "@jest/globals";
import {
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import {
  REMOTE_HISTORY_VIEW_PERMISSION,
  REMOTE_HISTORY_REPRINT_PERMISSION,
  RemoteHistoryPresenter,
  type RemoteHistoryReprintPort,
} from "./remote-history-presenter";
import {
  REMOTE_HISTORY_MIN_TOUCH_TARGET,
  RemoteHistoryScreen,
} from "./remote-history-screen";

import type { RemoteOrderHistoryPort } from "@/core/contracts/remote-history";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

const port: RemoteOrderHistoryPort = {
  async list() {
    return [
      {
        orderGuid: "10000000-0000-4000-8000-000000000001",
        storeCode: "S1",
        deviceCode: "IPAD-1",
        cashierName: "Alice",
        soldAtIso: "2026-07-27T01:02:03.000Z",
        totalCents: 1234,
        discountCents: 34,
        actualAmountCents: 1200,
        lineCount: 1,
        paymentSummary: "Card",
        statusLabel: "Synced",
      },
    ];
  },
  async getDetails() {
    return {
      orderGuid: "10000000-0000-4000-8000-000000000001",
      storeCode: "S1",
      deviceCode: "IPAD-1",
      cashierName: "Alice",
      soldAtIso: "2026-07-27T01:02:03.000Z",
      totalCents: 1234,
      discountCents: 34,
      actualAmountCents: 1200,
      lines: [
        {
          orderLineGuid: "20000000-0000-4000-8000-000000000001",
          productCode: "P1",
          referenceCode: null,
          displayName: "Tea",
          lookupCode: "930001",
          itemNumber: "I1",
          quantity: "1",
          unitPriceCents: 1234,
          discountCents: 34,
          actualAmountCents: 1200,
          kind: "sale",
        },
      ],
      payments: [
        {
          paymentGuid: "30000000-0000-4000-8000-000000000001",
          method: "card",
          amountCents: 1200,
          displayReference: null,
          cardType: "VISA",
          maskedCardNumber: "**** 1234",
        },
      ],
    };
  },
};

beforeEach(() => {
  jest.clearAllMocks();
});

test("横屏列表与详情可读，且不渲染退款、取单或重打入口", async () => {
  const presenter = new RemoteHistoryPresenter({
    port,
    trustedStoreCode: "S1",
    currentDeviceCode: "IPAD-1",
    online: true,
    permissionCodes: [REMOTE_HISTORY_VIEW_PERMISSION],
    now: () => new Date("2026-07-27T05:00:00Z"),
  });
  const screen = await render(
    <RemoteHistoryScreen onBack={jest.fn()} presenter={presenter} />,
  );

  await waitFor(() => {
    expect(screen.getByText("Tea")).toBeTruthy();
  });
  expect(screen.getByText("VISA · **** 1234")).toBeTruthy();
  expect(screen.queryByTestId("remote-history-refund")).toBeNull();
  expect(screen.queryByTestId("remote-history-recall")).toBeNull();
  expect(screen.queryByTestId("remote-history-reprint")).toBeNull();
  expect(screen.getByTestId("remote-history-readonly-note")).toBeTruthy();

  for (const testID of [
    "remote-history-back",
    "remote-history-refresh",
    "remote-history-apply-filters",
  ]) {
    const style = StyleSheet.flatten(screen.getByTestId(testID).props.style);
    expect(style.minHeight).toBeGreaterThanOrEqual(
      REMOTE_HISTORY_MIN_TOUCH_TARGET,
    );
  }
});

test("筛选只提交日期、终端和关键词，可信门店仍由 presenter 固定", async () => {
  const presenter = new RemoteHistoryPresenter({
    port,
    trustedStoreCode: "S1",
    currentDeviceCode: "IPAD-1",
    online: true,
    permissionCodes: [REMOTE_HISTORY_VIEW_PERMISSION],
    now: () => new Date("2026-07-27T05:00:00Z"),
  });
  const setFilters = jest.spyOn(presenter, "setFilters");
  const screen = await render(<RemoteHistoryScreen presenter={presenter} />);
  await waitFor(() => expect(screen.getByTestId("remote-history-list")).toBeTruthy());

  await fireEvent.changeText(
    screen.getByTestId("remote-history-device"),
    "",
  );
  await fireEvent.changeText(
    screen.getByTestId("remote-history-keyword"),
    "930001",
  );
  await fireEvent.press(
    screen.getByTestId("remote-history-apply-filters"),
  );

  expect(setFilters).toHaveBeenLastCalledWith(
    expect.objectContaining({
      deviceCode: null,
      keyword: "930001",
    }),
  );
});

test("离线状态明确说明远程历史只在线可用", async () => {
  const presenter = new RemoteHistoryPresenter({
    port,
    trustedStoreCode: "S1",
    currentDeviceCode: "IPAD-1",
    online: false,
    permissionCodes: [REMOTE_HISTORY_VIEW_PERMISSION],
    now: () => new Date("2026-07-27T05:00:00Z"),
  });
  const screen = await render(<RemoteHistoryScreen presenter={presenter} />);

  await waitFor(() => {
    expect(screen.getByTestId("remote-history-offline")).toBeTruthy();
  });
  expect(screen.getByText(/online only/i)).toBeTruthy();
});

test("仅在详情已可信加载并获重打权限时显示重打，退款入口仍不出现", async () => {
  const reprintPort: RemoteHistoryReprintPort = {
    reprintExistingOrder: jest.fn(async () => undefined),
  };
  const presenter = new RemoteHistoryPresenter({
    port,
    reprintPort,
    trustedStoreCode: "S1",
    currentDeviceCode: "IPAD-1",
    online: true,
    permissionCodes: [
      REMOTE_HISTORY_VIEW_PERMISSION,
      REMOTE_HISTORY_REPRINT_PERMISSION,
    ],
    now: () => new Date("2026-07-27T05:00:00Z"),
  });
  const screen = await render(<RemoteHistoryScreen presenter={presenter} />);

  const reprint = await screen.findByTestId("remote-history-reprint");
  await fireEvent.press(reprint);

  expect(reprintPort.reprintExistingOrder).toHaveBeenCalledWith(
    "10000000-0000-4000-8000-000000000001",
  );
  expect(screen.queryByTestId("remote-history-refund")).toBeNull();
});
