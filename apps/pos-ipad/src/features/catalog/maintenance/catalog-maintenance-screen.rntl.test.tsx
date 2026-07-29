import { afterEach, describe, expect, it, jest } from "@jest/globals";
import { act, fireEvent, render, waitFor } from "@testing-library/react-native";
import { StyleSheet } from "react-native";

import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressEvent,
  CatalogSummary,
} from "../catalog-refresh-contract";

import {
  CATALOG_MAINTENANCE_MIN_TOUCH_TARGET,
  CatalogMaintenancePresenter,
  CatalogMaintenanceScreen,
  type CatalogMaintenancePort,
} from "./index";

jest.mock("react-i18next", () => ({
  useTranslation: () => ({
    i18n: { language: "en", resolvedLanguage: "en" },
  }),
}));

const originalSummary: CatalogSummary = {
  snapshotId: "snapshot-ui-1",
  catalogVersion: "2026.07.28.1",
  itemCount: 42,
  activatedAt: "2026-07-28T04:10:00.000Z",
};

const replacementSummary: CatalogSummary = {
  snapshotId: "snapshot-ui-2",
  catalogVersion: "2026.07.29.1",
  itemCount: 45,
  activatedAt: "2026-07-29T04:10:00.000Z",
};

class ScreenCatalogMaintenancePort implements CatalogMaintenancePort {
  public calls = 0;
  public currentSummary: CatalogSummary | null = originalSummary;
  public hold: Promise<void> | null = null;
  public failure = false;
  public progressEvents: CatalogRefreshProgressEvent[] = [
    { step: "prepare", percent: 100 },
    {
      step: "products",
      percent: 50,
      completedItemCount: 21,
      totalItemCount: 42,
    },
  ];
  public completionProgressEvents: CatalogRefreshProgressEvent[] = [
    { step: "products", percent: 100 },
    { step: "promotions", percent: 100 },
    { step: "activate", percent: 100 },
  ];
  public outcome: CatalogRefreshOutcome = {
    kind: "complete",
    summary: replacementSummary,
  };

  public async getCurrentCatalog() {
    return this.currentSummary;
  }

  public async downloadAndActivate(input: Readonly<{
    storeCode: string;
    onProgress?(event: CatalogRefreshProgressEvent): void;
    signal?: AbortSignal;
  }>) {
    this.calls += 1;
    for (const event of this.progressEvents) input.onProgress?.(event);
    await this.hold;
    if (this.failure) throw new Error("HTTP 401 Bearer should-not-appear");
    for (const event of this.completionProgressEvents) input.onProgress?.(event);
    this.currentSummary = this.outcome.summary;
    return this.outcome;
  }
}

const presenters: CatalogMaintenancePresenter[] = [];

function createPresenter(port: CatalogMaintenancePort) {
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });
  presenters.push(presenter);
  return presenter;
}

async function renderInitialized(
  presenter: CatalogMaintenancePresenter,
  locale: "en" | "zh" = "en",
) {
  const screen = await render(
    <CatalogMaintenanceScreen locale={locale} presenter={presenter} />,
  );
  await act(async () => {
    await presenter.initialize();
  });
  return screen;
}

afterEach(() => {
  for (const presenter of presenters.splice(0)) presenter.destroy();
  jest.restoreAllMocks();
});

describe("CatalogMaintenanceScreen", () => {
  it("中文显示本地目录业务摘要、四步真实进度和 44pt 主操作", async () => {
    let release!: () => void;
    const port = new ScreenCatalogMaintenancePort();
    port.hold = new Promise<void>((resolve) => {
      release = resolve;
    });
    const screen = await renderInitialized(createPresenter(port), "zh");

    expect(screen.getByText("2026.07.28.1")).toBeTruthy();
    expect(screen.getByText("42")).toBeTruthy();
    expect(screen.getByText("snapshot-ui-1")).toBeTruthy();
    expect(screen.getByText("2026-07-28T04:10Z")).toBeTruthy();
    expect(
      StyleSheet.flatten(
        screen.getByTestId("catalog-maintenance-refresh").props.style,
      ).minHeight,
    ).toBeGreaterThanOrEqual(CATALOG_MAINTENANCE_MIN_TOUCH_TARGET);

    await act(async () => {
      fireEvent.press(screen.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await waitFor(() =>
      expect(
        screen.getByTestId("catalog-maintenance-downloading"),
      ).toBeTruthy(),
    );
    expect(screen.getByText("准备目录")).toBeTruthy();
    expect(screen.getByText("下载并校验商品")).toBeTruthy();
    expect(screen.getByText("同步促销")).toBeTruthy();
    expect(screen.getByText("安全激活")).toBeTruthy();
    expect(screen.getByText("总进度：37.5%")).toBeTruthy();
    expect(
      screen.getByTestId("catalog-maintenance-overall-progress").props
        .accessibilityValue,
    ).toEqual({ max: 100, min: 0, now: 37.5 });
    expect(
      screen.getByTestId("catalog-maintenance-refresh").props
        .accessibilityState,
    ).toEqual({ disabled: true });
    expect(screen.getByText("2026.07.28.1")).toBeTruthy();

    await act(async () => {
      release();
      await Promise.resolve();
    });
    await screen.findByTestId("catalog-maintenance-success");
    expect(screen.getByText("2026.07.29.1")).toBeTruthy();
  });

  it("英文刷新失败不泄漏底层异常，且保留当前摘要与重试操作", async () => {
    const port = new ScreenCatalogMaintenancePort();
    port.failure = true;
    const screen = await renderInitialized(createPresenter(port));

    await act(async () => {
      fireEvent.press(screen.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await screen.findByTestId("catalog-maintenance-failed");
    expect(screen.getByText("Safe error: catalog-refresh-failed")).toBeTruthy();
    expect(screen.queryByText(/should-not-appear/)).toBeNull();
    expect(screen.getByText("2026.07.28.1")).toBeTruthy();
    expect(
      screen.getByTestId("catalog-maintenance-refresh").props
        .accessibilityState,
    ).toEqual({ disabled: false });
  });

  it("warning 显示新 active 摘要与准确运行时提示，而非旧目录连续性文案", async () => {
    const port = new ScreenCatalogMaintenancePort();
    port.outcome = {
      kind: "activated-with-warning",
      summary: replacementSummary,
      warningCode: "catalog-runtime-reload-failed",
    };
    const screen = await renderInitialized(createPresenter(port));

    await act(async () => {
      fireEvent.press(screen.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await screen.findByTestId("catalog-maintenance-warning");
    expect(screen.getByText("2026.07.29.1")).toBeTruthy();
    expect(
      screen.getByText(
        "The new catalog is activated, but the runtime catalog reload did not complete. Do not continue checkout; retry refresh or contact support.",
      ),
    ).toBeTruthy();
    expect(
      screen.queryByText("The existing catalog remains available."),
    ).toBeNull();
    expect(screen.queryByTestId("catalog-maintenance-failed")).toBeNull();
  });

  it("activation verification warning 显示对应提示", async () => {
    const port = new ScreenCatalogMaintenancePort();
    port.outcome = {
      kind: "activated-with-warning",
      summary: replacementSummary,
      warningCode: "catalog-activation-verification-failed",
    };
    const screen = await renderInitialized(createPresenter(port));

    await act(async () => {
      fireEvent.press(screen.getByTestId("catalog-maintenance-refresh"));
      await Promise.resolve();
    });
    await screen.findByTestId("catalog-maintenance-warning");
    expect(
      screen.getByText(
        "Catalog activation was committed, but local confirmation did not complete. Do not continue checkout; contact support.",
      ),
    ).toBeTruthy();
  });

  it("没有本地目录时显示安全状态并允许刷新", async () => {
    const port = new ScreenCatalogMaintenancePort();
    port.currentSummary = null;
    const screen = await renderInitialized(createPresenter(port), "zh");

    expect(
      screen.getByTestId("catalog-maintenance-catalog-unavailable"),
    ).toBeTruthy();
    expect(screen.getByText("暂无本地目录")).toBeTruthy();
    expect(
      screen.getByTestId("catalog-maintenance-refresh").props
        .accessibilityState,
    ).toEqual({ disabled: false });
  });

  it("可选返回按钮保留至少 44pt 触控目标", async () => {
    const onBack = jest.fn();
    const presenter = createPresenter(new ScreenCatalogMaintenancePort());
    const screen = await render(
      <CatalogMaintenanceScreen onBack={onBack} presenter={presenter} />,
    );
    const back = screen.getByTestId("catalog-maintenance-back");
    expect(
      StyleSheet.flatten(back.props.style).minHeight,
    ).toBeGreaterThanOrEqual(CATALOG_MAINTENANCE_MIN_TOUCH_TARGET);
    fireEvent.press(back);
    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
