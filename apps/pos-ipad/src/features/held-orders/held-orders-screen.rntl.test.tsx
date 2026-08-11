import { afterEach, beforeEach, expect, jest, test } from "@jest/globals";
import {
  act,
  cleanup,
  fireEvent,
  render,
  waitFor,
} from "@testing-library/react-native";
import {
  AppState,
  type AppStateEvent,
  type AppStateStatus,
} from "react-native";

import {
  HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS,
  HELD_ORDERS_MIN_TOUCH_TARGET,
  HeldOrdersScreen,
} from "./held-orders-screen";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({ i18n: { language: "en", resolvedLanguage: "en" } }),
}));

let appStateListener: ((state: AppStateStatus) => void) | null = null;

beforeEach(() => {
  appStateListener = null;
  jest.spyOn(AppState, "addEventListener").mockImplementation(
    (eventName: AppStateEvent, handler: (state: AppStateStatus) => void) => {
      if (eventName === "change") {
        appStateListener = handler;
      }
      return { remove: jest.fn() } as never;
    },
  );
});

afterEach(async () => {
  jest.useRealTimers();
  jest.restoreAllMocks();
  await cleanup();
});

function localSummary(overrides: Record<string, unknown> = {}) {
  return {
    holdId: "hold-1",
    localSequence: 8,
    scope: { storeCode: "BNE", deviceCode: "IPAD-1" },
    heldBy: { cashierId: "C1", cashierName: "Cashier" },
    status: "Pending",
    itemCount: 2,
    subtotalCents: 1_200,
    discountCents: 0,
    actualAmountCents: 1_200,
    heldAtIso: "2026-07-28T01:00:00.000Z",
    recallingAtIso: null,
    ...overrides,
  };
}

function viewRow(overrides: Record<string, unknown> = {}) {
  return {
    holdId: "hold-1",
    local: localSummary(),
    remote: null,
    status: "local-pending",
    blockReason: null,
    ...overrides,
  };
}

function createPresenter(overrides: Record<string, unknown> = {}) {
  const listeners = new Set<() => void>();
  const state: any = overrides.state ?? {
    kind: "ready",
    rows: [],
    busy: false,
    lastAction: null,
    refreshError: null,
    sharedEnabled: false,
  };
  let timer: ReturnType<typeof setInterval> | null = null;
  const presenter: any = {
    state,
    getState: () => state,
    subscribe: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    notify: () => {
      for (const listener of [...listeners]) listener();
    },
    refresh: jest.fn(async () => undefined),
    hold: jest.fn(async () => ({ ok: true, code: "held" })),
    recall: jest.fn(async () => ({ ok: true, code: "recalled" })),
    recover: jest.fn(async () => ({ ok: true, code: "recovered" })),
    release: jest.fn(async () => ({ ok: true, code: "released" })),
    takeRemote: jest.fn(async () => ({ ok: true, code: "recalled" })),
    recallLocalShared: jest.fn(async () => ({ ok: true, code: "recalled" })),
    forceRelease: jest.fn(async () => ({ ok: true, code: "force-released" })),
    supportsForceRelease: () => false,
    startAutoRefresh: jest.fn((intervalMs: number) => {
      if (timer) return;
      timer = setInterval(() => {
        void presenter.refresh();
      }, intervalMs);
    }),
    stopAutoRefresh: jest.fn(() => {
      if (timer) {
        clearInterval(timer);
        timer = null;
      }
    }),
    ...overrides,
  };
  return presenter;
}

test("横屏挂单列表提供 44pt 操作、显式恢复入口和不含商品详情的行", async () => {
  expect(HELD_ORDERS_MIN_TOUCH_TARGET).toBe(44);
  const presenter = createPresenter({
    state: {
      kind: "ready",
      busy: false,
      lastAction: null,
      refreshError: null,
      sharedEnabled: false,
      rows: [
        viewRow({
          holdId: "recover-1",
          local: localSummary({
            holdId: "recover-1",
            status: "Recalling",
            recallingAtIso: "2026-07-28T01:01:00.000Z",
          }),
          status: "claiming-here",
        }),
      ],
    },
  });
  const onBack = jest.fn();
  const screen = await render(
    <HeldOrdersScreen onBack={onBack} presenter={presenter} />,
  );
  await waitFor(() => expect(presenter.refresh).toHaveBeenCalledTimes(1));
  expect(screen.getByTestId("held-order-row-recover-1")).toBeTruthy();
  expect(screen.queryByText("Product 1")).toBeNull();
  await fireEvent.press(screen.getByTestId("held-order-action-recover-1"));
  expect(presenter.recover).toHaveBeenCalledWith("recover-1");
  await waitFor(() => expect(onBack).toHaveBeenCalledTimes(1));
  expect(screen.getByTestId("held-orders-hold").props.accessibilityState.disabled).toBe(false);
});

test("合并列表按 HoldGuid 去重并显示本地/共享/远端/阻断徽标与远端来源", async () => {
  const presenter = createPresenter({
    state: {
      kind: "ready",
      busy: false,
      lastAction: null,
      refreshError: null,
      sharedEnabled: true,
      rows: [
        viewRow({
          holdId: "shared-1",
          local: localSummary({ holdId: "shared-1" }),
          remote: {
            holdGuid: "shared-1",
            deviceCode: "IPAD-1",
            cashierName: "Cashier",
            heldAtIso: "2026-07-28T01:00:00.000Z",
            lineCount: 2,
            actualCents: 1_200,
          },
          status: "published-shareable",
        }),
        viewRow({
          holdId: "remote-2",
          local: null,
          remote: {
            holdGuid: "remote-2",
            deviceCode: "IPAD-2",
            cashierName: "Other Cashier",
            heldAtIso: "2026-07-28T02:00:00.000Z",
            lineCount: 3,
            actualCents: 3_300,
          },
          status: "remote-pending",
        }),
        viewRow({
          holdId: "local-3",
          local: localSummary({ holdId: "local-3" }),
          status: "local-pending-publish",
        }),
        viewRow({
          holdId: "blocked-4",
          local: localSummary({ holdId: "blocked-4" }),
          status: "blocked",
          blockReason: "LEGACY_PAYLOAD_CORRUPTED",
        }),
      ],
    },
  });
  const screen = await render(<HeldOrdersScreen presenter={presenter} />);

  expect(screen.getByText("Published · shareable")).toBeTruthy();
  expect(screen.getByText("Remote · available")).toBeTruthy();
  expect(screen.getByText("Device IPAD-2 · Other Cashier")).toBeTruthy();
  expect(screen.getByText("Local · awaiting share")).toBeTruthy();
  expect(screen.getByText("Sharing blocked")).toBeTruthy();
  expect(screen.getByTestId("held-order-blocked-reason-blocked-4")).toBeTruthy();
  expect(
    screen.getByText("Legacy hold data could not be read losslessly."),
  ).toBeTruthy();
});

test("远端挂单通过本机在线取单，成功返回收银", async () => {
  const presenter = createPresenter({
    state: {
      kind: "ready",
      busy: false,
      lastAction: null,
      refreshError: null,
      sharedEnabled: true,
      rows: [
        viewRow({
          holdId: "remote-2",
          local: null,
          remote: {
            holdGuid: "remote-2",
            deviceCode: "IPAD-2",
            cashierName: "Other Cashier",
            heldAtIso: "2026-07-28T02:00:00.000Z",
            lineCount: 3,
            actualCents: 3_300,
          },
          status: "remote-pending",
        }),
      ],
    },
  });
  const onBack = jest.fn();
  const screen = await render(
    <HeldOrdersScreen onBack={onBack} presenter={presenter} />,
  );
  await fireEvent.press(screen.getByTestId("held-order-action-remote-2"));
  expect(presenter.takeRemote).toHaveBeenCalledWith("remote-2");
  await waitFor(() => expect(onBack).toHaveBeenCalledTimes(1));
});

test("本机可共享副本通过 OfflineOrigin durable claim 取回，不走旧 recall", async () => {
  const presenter = createPresenter({
    state: {
      kind: "ready",
      busy: false,
      lastAction: null,
      refreshError: null,
      sharedEnabled: true,
      rows: [
        viewRow({
          holdId: "shared-local",
          local: localSummary({ holdId: "shared-local" }),
          status: "published-shareable",
        }),
      ],
    },
  });
  const onBack = jest.fn();
  const screen = await render(
    <HeldOrdersScreen onBack={onBack} presenter={presenter} />,
  );

  await fireEvent.press(screen.getByTestId("held-order-action-shared-local"));

  expect(presenter.recallLocalShared).toHaveBeenCalledWith("shared-local");
  expect(presenter.recall).not.toHaveBeenCalled();
  await waitFor(() => expect(onBack).toHaveBeenCalledTimes(1));
});

test("共享同步失败保留本地行并显示非阻塞错误", async () => {
  const presenter = createPresenter({
    state: {
      kind: "ready",
      busy: false,
      lastAction: null,
      refreshError: "SHARED_HELD_ORDERS_SYNC_FAILED",
      sharedEnabled: true,
      rows: [viewRow({ holdId: "local-1" })],
    },
  });
  const screen = await render(<HeldOrdersScreen presenter={presenter} />);
  expect(screen.getByTestId("held-orders-refresh-error")).toBeTruthy();
  expect(screen.getByTestId("held-order-row-local-1")).toBeTruthy();
});

test("页面可见时每 10 秒刷新，进入后台停表，回到前台恢复，卸载停表", async () => {
  jest.useFakeTimers();
  const presenter = createPresenter();
  const screen = await render(<HeldOrdersScreen presenter={presenter} />);
  await act(async () => {});
  expect(presenter.refresh).toHaveBeenCalledTimes(1);

  await act(async () => {
    jest.advanceTimersByTime(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS);
  });
  expect(presenter.refresh).toHaveBeenCalledTimes(2);

  await act(async () => {
    appStateListener?.("background");
  });
  await act(async () => {
    jest.advanceTimersByTime(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS * 3);
  });
  expect(presenter.stopAutoRefresh).toHaveBeenCalled();
  expect(presenter.refresh).toHaveBeenCalledTimes(2);

  await act(async () => {
    appStateListener?.("active");
  });
  await act(async () => {
    jest.advanceTimersByTime(HELD_ORDERS_AUTO_REFRESH_INTERVAL_MS);
  });
  expect(presenter.refresh).toHaveBeenCalledTimes(3);

  await act(async () => {
    await screen.unmount();
  });
  expect(presenter.stopAutoRefresh).toHaveBeenCalledTimes(2);
});

test("强制释放入口未接线时不出现", async () => {
  const claimingRow = viewRow({
    holdId: "claim-1",
    local: localSummary({
      holdId: "claim-1",
      status: "Recalling",
      recallingAtIso: "2026-07-28T01:01:00.000Z",
    }),
    status: "claiming-here",
  });
  const base = {
    kind: "ready",
    busy: false,
    lastAction: null,
    refreshError: null,
    sharedEnabled: true,
    rows: [claimingRow],
  };

  const screen = await render(
    <HeldOrdersScreen
      presenter={createPresenter({ state: base, supportsForceRelease: () => false })}
    />,
  );
  expect(screen.queryByTestId("held-order-force-release-claim-1")).toBeNull();
});

test("强制释放接线后显示入口并要求非空原因", async () => {
  const claimingRow = viewRow({
    holdId: "claim-1",
    local: localSummary({
      holdId: "claim-1",
      status: "Recalling",
      recallingAtIso: "2026-07-28T01:01:00.000Z",
    }),
    status: "claiming-here",
  });
  const presenter = createPresenter({
    state: {
      kind: "ready",
      busy: false,
      lastAction: null,
      refreshError: null,
      sharedEnabled: true,
      rows: [claimingRow],
    },
    supportsForceRelease: () => true,
  });
  const screen = await render(<HeldOrdersScreen presenter={presenter} />);
  await fireEvent.press(screen.getByTestId("held-order-force-release-claim-1"));
  expect(screen.getByTestId("held-orders-force-release-panel")).toBeTruthy();
  expect(
    screen.getByTestId("held-orders-force-release-panel").props
      .automaticallyAdjustKeyboardInsets,
  ).toBe(true);

  const confirm = screen.getByTestId("held-orders-force-release-confirm");
  expect(confirm.props.accessibilityState.disabled).toBe(true);
  await fireEvent.changeText(
    screen.getByTestId("held-orders-force-release-reason"),
    "  duplicate claim  ",
  );
  expect(confirm.props.accessibilityState.disabled).toBe(false);
  await fireEvent.press(confirm);
  expect(presenter.forceRelease).toHaveBeenCalledWith("claim-1", "duplicate claim");
  expect(screen.queryByTestId("held-orders-force-release-panel")).toBeNull();
});
