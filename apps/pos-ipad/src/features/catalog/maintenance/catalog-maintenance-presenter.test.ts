import assert from "node:assert/strict";
import test from "node:test";

import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressEvent,
  CatalogSummary,
} from "../catalog-refresh-contract";

import {
  CatalogMaintenancePresenter,
  type CatalogMaintenancePort,
} from "./catalog-maintenance-presenter";

const originalSummary: CatalogSummary = {
  snapshotId: "catalog-20260728-1",
  catalogVersion: "2026.07.28.1",
  itemCount: 128,
  activatedAt: "2026-07-28T04:10:00.000Z",
};

const replacementSummary: CatalogSummary = {
  snapshotId: "catalog-20260729-1",
  catalogVersion: "2026.07.29.1",
  itemCount: 130,
  activatedAt: "2026-07-29T04:10:00.000Z",
};

class MemoryCatalogMaintenancePort implements CatalogMaintenancePort {
  public readonly storeCodes: string[] = [];
  public currentSummary: CatalogSummary | null = originalSummary;
  public currentFailure: unknown = null;
  public downloadFailure: unknown = null;
  public hold: Promise<void> | null = null;
  public readonly progressEvents: CatalogRefreshProgressEvent[] = [];
  public readonly completionProgressEvents: CatalogRefreshProgressEvent[] = [];
  public outcome: CatalogRefreshOutcome = {
    kind: "complete",
    summary: replacementSummary,
  };
  public receivedSignal: AbortSignal | undefined;

  public async getCurrentCatalog() {
    if (this.currentFailure) throw this.currentFailure;
    return this.currentSummary;
  }

  public async downloadAndActivate(input: Readonly<{
    storeCode: string;
    onProgress?(event: CatalogRefreshProgressEvent): void;
    signal?: AbortSignal;
  }>) {
    this.storeCodes.push(input.storeCode);
    this.receivedSignal = input.signal;
    for (const event of this.progressEvents) input.onProgress?.(event);
    await this.hold;
    if (this.downloadFailure) throw this.downloadFailure;
    for (const event of this.completionProgressEvents) input.onProgress?.(event);
    this.currentSummary = this.outcome.summary;
    return this.outcome;
  }
}

test("初始化读取当前本地目录摘要", async () => {
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port: new MemoryCatalogMaintenancePort(),
  });

  await presenter.initialize();

  assert.deepEqual(presenter.getState(), {
    catalog: { kind: "ready", summary: originalSummary },
    refresh: { kind: "idle" },
  });
});

test("刷新只把已认证固定门店传给窄 Port，并保留旧摘要与真实进度", async () => {
  let release!: () => void;
  const port = new MemoryCatalogMaintenancePort();
  port.hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  port.progressEvents.push(
    { step: "prepare", percent: 100 },
    {
      step: "products",
      percent: 25,
      completedItemCount: 32,
      totalItemCount: 128,
    },
  );
  port.completionProgressEvents.push(
    { step: "products", percent: 100 },
    { step: "promotions", percent: 100 },
    { step: "activate", percent: 100 },
  );
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });
  await presenter.initialize();

  const refreshing = presenter.refresh();
  await Promise.resolve();
  const duringRefresh = presenter.getState();
  assert.deepEqual(port.storeCodes, ["BNE-01"]);
  assert.equal(duringRefresh.catalog.summary, originalSummary);
  assert.equal(duringRefresh.refresh.kind, "running");
  if (duringRefresh.refresh.kind === "running") {
    assert.equal(duringRefresh.refresh.progress.overallPercent, 31.25);
    assert.deepEqual(duringRefresh.refresh.progress.steps[1], {
      step: "products",
      percent: 25,
      completedItemCount: 32,
      totalItemCount: 128,
    });
  }

  release();
  await refreshing;
  assert.deepEqual(presenter.getState().catalog, {
    kind: "ready",
    summary: replacementSummary,
  });
  assert.equal(presenter.getState().refresh.kind, "success");
});

test("重复点击刷新单飞，只发起一次下载", async () => {
  let release!: () => void;
  const port = new MemoryCatalogMaintenancePort();
  port.hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  const first = presenter.refresh();
  const second = presenter.refresh();
  assert.equal(first, second);
  await Promise.resolve();
  assert.deepEqual(port.storeCodes, ["BNE-01"]);

  release();
  await first;
  assert.equal(presenter.getState().refresh.kind, "success");
});

test("忽略倒退或跳过前置步骤的进度，页面不会伪造完成状态", async () => {
  let release!: () => void;
  const port = new MemoryCatalogMaintenancePort();
  port.hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  port.progressEvents.push(
    { step: "products", percent: 30 },
    { step: "prepare", percent: 100 },
    { step: "products", percent: 60 },
    { step: "prepare", percent: 90 },
  );
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  const refreshing = presenter.refresh();
  await Promise.resolve();
  const state = presenter.getState();
  assert.equal(state.refresh.kind, "running");
  if (state.refresh.kind === "running") {
    assert.deepEqual(
      state.refresh.progress.steps.map((step) => step.percent),
      [100, 60, 0, 0],
    );
  }
  release();
  await refreshing;
});

test("底层刷新异常被收敛为稳定安全错误码，并复读真实 active 摘要", async () => {
  const port = new MemoryCatalogMaintenancePort();
  port.downloadFailure = new Error(
    "GET https://api.example.test failed; bearer secret-token",
  );
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });
  await presenter.initialize();

  await presenter.refresh();

  assert.deepEqual(presenter.getState().catalog, {
    kind: "ready",
    summary: originalSummary,
  });
  const refresh = presenter.getState().refresh;
  assert.equal(refresh.kind, "failed");
  if (refresh.kind === "failed") {
    assert.equal(refresh.errorCode, "catalog-refresh-failed");
  }
});

test("已激活但后续运行时未完成时保留新摘要并进入 warning，而非普通失败", async () => {
  const port = new MemoryCatalogMaintenancePort();
  port.outcome = {
    kind: "activated-with-warning",
    summary: replacementSummary,
    warningCode: "catalog-runtime-reload-failed",
  };
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });
  await presenter.initialize();

  await presenter.refresh();

  assert.deepEqual(presenter.getState().catalog, {
    kind: "ready",
    summary: replacementSummary,
  });
  const refresh = presenter.getState().refresh;
  assert.equal(refresh.kind, "warning");
  if (refresh.kind === "warning") {
    assert.equal(refresh.warningCode, "catalog-runtime-reload-failed");
  }
});

test("销毁 presenter 会中止在途刷新，离页后不发布完成状态", async () => {
  let release!: () => void;
  const port = new MemoryCatalogMaintenancePort();
  port.hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  const refreshing = presenter.refresh();
  await Promise.resolve();
  const stateBeforeDestroy = presenter.getState();
  presenter.destroy();
  assert.equal(port.receivedSignal?.aborted, true);

  release();
  await refreshing;
  assert.deepEqual(presenter.getState(), stateBeforeDestroy);
});

test("初始元数据读取失败只显示稳定码，仍允许后续刷新", async () => {
  const port = new MemoryCatalogMaintenancePort();
  port.currentFailure = new Error("https://api.example.test bearer secret-token");
  const presenter = new CatalogMaintenancePresenter({
    authenticatedStoreCode: "BNE-01",
    port,
  });

  await presenter.initialize();
  assert.deepEqual(presenter.getState().catalog, {
    kind: "failed",
    summary: null,
    errorCode: "catalog-metadata-unavailable",
  });

  port.currentFailure = null;
  await presenter.refresh();
  assert.equal(presenter.getState().refresh.kind, "success");
});
