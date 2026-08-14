import assert from "node:assert/strict";
import test from "node:test";

import type {
  CatalogRefreshOutcome,
  CatalogRefreshProgressEvent,
  CatalogSummary,
} from "./catalog-refresh-contract";
import {
  CatalogRefreshCoordinator,
  CatalogRefreshCoordinatorError,
} from "./catalog-refresh-coordinator";
import { CatalogSnapshotFailure } from "./catalog-snapshot-service";

import { HbposApiError } from "@/core/api";

const summary: CatalogSummary = {
  snapshotId: "catalog-20260729-1",
  catalogVersion: "2026.07.29.1",
  itemCount: 130,
  activatedAt: "2026-07-29T04:10:00.000Z",
};

test("同店重复启动共享同一任务、状态和真实进度", async () => {
  let release!: () => void;
  let entered!: () => void;
  let calls = 0;
  const started = new Promise<void>((resolve) => {
    entered = resolve;
  });
  const hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new CatalogRefreshCoordinator();
  const execute = async ({
    onProgress,
  }: Readonly<{
    signal: AbortSignal;
    onProgress(event: CatalogRefreshProgressEvent): void;
  }>): Promise<CatalogRefreshOutcome> => {
    calls += 1;
    onProgress({ step: "prepare", percent: 100 });
    onProgress({
      step: "products",
      percent: 25,
      completedItemCount: 32,
      totalItemCount: 128,
      completedPageCount: 1,
      totalPageCount: 4,
    });
    entered();
    await hold;
    onProgress({ step: "products", percent: 100 });
    onProgress({ step: "promotions", percent: 100 });
    onProgress({ step: "activate", percent: 100 });
    return { kind: "complete", summary };
  };

  const first = coordinator.start({ storeCode: " BNE-01 ", execute });
  await started;
  const second = coordinator.start({ storeCode: "BNE-01", execute });

  assert.equal(first, second);
  assert.equal(calls, 1);
  const running = coordinator.getState();
  assert.equal(running.kind, "running");
  if (running.kind === "running") {
    assert.equal(running.storeCode, "BNE-01");
    assert.equal(running.progress.overallPercent, 31.25);
    assert.deepEqual(running.progress.steps[1], {
      step: "products",
      percent: 25,
      completedItemCount: 32,
      totalItemCount: 128,
      completedPageCount: 1,
      totalPageCount: 4,
    });
  }

  release();
  await first;
  const complete = coordinator.getState();
  assert.equal(complete.kind, "success");
  if (complete.kind === "success") {
    assert.equal(complete.storeCode, "BNE-01");
    assert.equal(complete.summary, summary);
    assert.equal(complete.progress.currentStep, "activate");
    assert.equal(complete.progress.overallPercent, 100);
    assert.ok(complete.progress.elapsedMilliseconds >= 0);
    assert.deepEqual(complete.progress.steps, [
      { step: "prepare", percent: 100 },
      {
        step: "products",
        percent: 100,
        completedItemCount: 32,
        totalItemCount: 128,
        completedPageCount: 1,
        totalPageCount: 4,
      },
      { step: "promotions", percent: 100 },
      { step: "activate", percent: 100 },
    ]);
  }
});

test("running 发布和 execute 同步重入仍返回同一任务", async () => {
  const coordinator = new CatalogRefreshCoordinator();
  let calls = 0;
  let listenerJoin: Promise<CatalogRefreshOutcome> | null = null;
  let executeJoin: Promise<CatalogRefreshOutcome> | null = null;
  const execute = async (): Promise<CatalogRefreshOutcome> => {
    calls += 1;
    executeJoin ??= coordinator.start({
      storeCode: "BNE-01",
      execute,
    });
    return { kind: "complete", summary };
  };
  coordinator.subscribe(() => {
    if (
      coordinator.getState().kind === "running" &&
      listenerJoin === null
    ) {
      listenerJoin = coordinator.start({
        storeCode: "BNE-01",
        execute,
      });
    }
  });

  const first = coordinator.start({
    storeCode: "BNE-01",
    execute,
  });
  await first;

  assert.equal(listenerJoin, first);
  assert.equal(executeJoin, first);
  assert.equal(calls, 1);
});

test("危险运行时动作与目录刷新使用同一互斥门闩", async () => {
  let release!: () => void;
  let entered!: () => void;
  const started = new Promise<void>((resolve) => {
    entered = resolve;
  });
  const hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new CatalogRefreshCoordinator();
  const dangerous = coordinator.runExclusive(async () => {
    entered();
    await hold;
    return "done";
  });
  await started;

  await assert.rejects(
    () =>
      coordinator.start({
        storeCode: "BNE-01",
        execute: async () => ({ kind: "complete", summary }),
      }),
    (error: unknown) =>
      error instanceof CatalogRefreshCoordinatorError &&
      error.code === "CATALOG_REFRESH_OPERATION_CONFLICT",
  );
  release();
  assert.equal(await dangerous, "done");

  let releaseRefresh!: () => void;
  const refreshHold = new Promise<void>((resolve) => {
    releaseRefresh = resolve;
  });
  const refresh = coordinator.start({
    storeCode: "BNE-01",
    execute: async () => {
      await refreshHold;
      return { kind: "complete", summary };
    },
  });
  await assert.rejects(
    () => coordinator.runExclusive(async () => undefined),
    (error: unknown) =>
      error instanceof CatalogRefreshCoordinatorError &&
      error.code === "CATALOG_REFRESH_OPERATION_CONFLICT",
  );
  releaseRefresh();
  await refresh;
});

test("不同门店不能加入在途任务且不会覆盖原状态", async () => {
  let release!: () => void;
  let entered!: () => void;
  const started = new Promise<void>((resolve) => {
    entered = resolve;
  });
  const hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new CatalogRefreshCoordinator();
  const first = coordinator.start({
    storeCode: "BNE-01",
    execute: async () => {
      entered();
      await hold;
      return { kind: "complete", summary };
    },
  });
  await started;
  const beforeConflict = coordinator.getState();

  await assert.rejects(
    () =>
      coordinator.start({
        storeCode: "SYD-02",
        execute: async () => ({ kind: "complete", summary }),
      }),
    (error: unknown) =>
      error instanceof CatalogRefreshCoordinatorError &&
      error.code === "CATALOG_REFRESH_STORE_CONFLICT",
  );
  assert.equal(coordinator.getState(), beforeConflict);

  release();
  await first;
});

test("首包前只推进真实耗时，不伪造准备百分比", async () => {
  let now = 1_000;
  let release!: () => void;
  let entered!: () => void;
  const started = new Promise<void>((resolve) => {
    entered = resolve;
  });
  const hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new CatalogRefreshCoordinator({
    elapsedIntervalMilliseconds: 1,
    nowMilliseconds: () => now,
  });
  const refresh = coordinator.start({
    storeCode: "BNE-01",
    execute: async () => {
      entered();
      await hold;
      return { kind: "complete", summary };
    },
  });
  await started;
  now = 3_250;
  await new Promise((resolve) => setTimeout(resolve, 5));

  const running = coordinator.getState();
  assert.equal(running.kind, "running");
  if (running.kind === "running") {
    assert.equal(running.progress.elapsedMilliseconds, 2_250);
    assert.equal(running.progress.overallPercent, 0);
    assert.deepEqual(
      running.progress.steps.map((step) => step.percent),
      [0, 0, 0, 0],
    );
  }

  release();
  await refresh;
});

test("稳定传输、API 与目录校验错误在共享状态中脱敏", async () => {
  const cases = [
    [
      new HbposApiError("https://api.example.test bearer secret", {
        kind: "transport",
      }),
      "catalog-refresh-network-failed",
    ],
    [
      new HbposApiError("HTTP 503 from https://api.example.test", {
        kind: "http",
        status: 503,
      }),
      "catalog-refresh-api-rejected",
    ],
    [
      Object.assign(new Error("checksum secret details"), {
        code: "CATALOG_PAGE_CHECKSUM_MISMATCH",
      }),
      "catalog-refresh-verification-failed",
    ],
  ] as const;

  for (const [failure, errorCode] of cases) {
    const coordinator = new CatalogRefreshCoordinator();
    await assert.rejects(
      () =>
        coordinator.start({
          storeCode: "BNE-01",
          execute: async () => {
            throw failure;
          },
        }),
      (error: unknown) => error === failure,
    );
    const state = coordinator.getState();
    assert.equal(state.kind, "failed");
    if (state.kind === "failed") assert.equal(state.errorCode, errorCode);
  }
});

test("目录失败只输出安全分页诊断且共享状态继续使用通用错误码", async () => {
  const diagnostics: unknown[] = [];
  const coordinator = new CatalogRefreshCoordinator({
    onDiagnostic: (event) => diagnostics.push(event),
  });
  const failure = new CatalogSnapshotFailure({
    code: "CATALOG_PAGE_CHECKSUM_MISMATCH",
    pageNumber: 9,
    completedItemCount: 40_000,
    totalItemCount: 344_665,
    httpStatus: 200,
  });

  await assert.rejects(
    () => coordinator.start({
      storeCode: "BNE-01",
      execute: async () => {
        throw failure;
      },
    }),
    (error: unknown) => error === failure,
  );

  assert.deepEqual(diagnostics, [{
    storeCode: "BNE-01",
    code: "CATALOG_PAGE_CHECKSUM_MISMATCH",
    pageNumber: 9,
    completedItemCount: 40_000,
    totalItemCount: 344_665,
    httpStatus: 200,
  }]);
  assert.equal(JSON.stringify(diagnostics).includes("message"), false);
  const state = coordinator.getState();
  assert.equal(state.kind, "failed");
  if (state.kind === "failed") {
    assert.equal(state.errorCode, "catalog-refresh-verification-failed");
  }
});

test("默认目录诊断使用 Handheld 身份且不输出底层异常正文", async () => {
  const originalConsoleError = console.error;
  const logged: unknown[][] = [];
  console.error = (...values: unknown[]) => logged.push(values);
  const failure = new CatalogSnapshotFailure({
    code: "CATALOG_PAGE_CHECKSUM_MISMATCH",
    pageNumber: 2,
    completedItemCount: 100,
    totalItemCount: 200,
    httpStatus: 200,
  });

  try {
    const coordinator = new CatalogRefreshCoordinator();
    await assert.rejects(
      () =>
        coordinator.start({
          storeCode: "BNE-01",
          execute: async () => {
            throw failure;
          },
        }),
      (error: unknown) => error === failure,
    );
  } finally {
    console.error = originalConsoleError;
  }

  assert.equal(logged.length, 1);
  assert.equal(logged[0]?.[0], "[HBPOS][Handheld][CatalogRefresh]");
  assert.equal(String(logged[0]?.[1]).includes("message"), false);
});

test("shutdown 会中止并等待在途任务，不把取消发布为失败", async () => {
  let entered!: () => void;
  let release!: () => void;
  let observedSignal: AbortSignal | null = null;
  const started = new Promise<void>((resolve) => {
    entered = resolve;
  });
  const hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  const coordinator = new CatalogRefreshCoordinator();
  const refresh = coordinator.start({
    storeCode: "BNE-01",
    execute: async ({ signal }) => {
      observedSignal = signal;
      entered();
      await hold;
      if (signal.aborted) {
        throw Object.assign(new Error("cancelled"), { name: "AbortError" });
      }
      return { kind: "complete", summary };
    },
  });
  await started;

  let shutdownResolved = false;
  const shutdown = coordinator.shutdown().then(() => {
    shutdownResolved = true;
  });
  assert.equal((observedSignal as unknown as AbortSignal).aborted, true);
  await Promise.resolve();
  assert.equal(shutdownResolved, false);

  release();
  await assert.rejects(refresh, { name: "AbortError" });
  await shutdown;
  assert.deepEqual(coordinator.getState(), { kind: "idle" });
  await assert.rejects(
    () =>
      coordinator.start({
        storeCode: "BNE-01",
        execute: async () => ({ kind: "complete", summary }),
      }),
    (error: unknown) =>
      error instanceof CatalogRefreshCoordinatorError &&
      error.code === "CATALOG_REFRESH_COORDINATOR_SHUTDOWN",
  );
});
