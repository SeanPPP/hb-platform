import assert from "node:assert/strict";
import test from "node:test";
import {
  classifyOfflineCatalogRefreshError,
  OfflineCatalogRefreshCoordinator,
  type OfflineCatalogRefreshState,
} from "./offline-catalog-refresh-coordinator";
import type { OfflineCatalogRefreshResult } from "./offline-catalog-sync-service";
import { OfflineCatalogError } from "./types";

test("目录准备超时映射到可操作的错误提示", () => {
  assert.equal(
    classifyOfflineCatalogRefreshError(
      new OfflineCatalogError("preparation timed out", "OFFLINE_CATALOG_PREPARATION_TIMEOUT"),
    ),
    "preparationTimeout",
  );
});

function buildResult(storeCode: string, itemCount: number): OfflineCatalogRefreshResult {
  return {
    mode: "full",
    metadata: {
      snapshotId: `snap-${storeCode}`,
      storeCode,
      catalogVersion: `catalog-v1:${storeCode}`,
      itemCount,
      generatedAt: "2026-09-18T00:00:00.000Z",
      activatedAt: "2026-09-18T00:00:02.000Z",
    },
  };
}

test("同店重复 start 复用同一次下载", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  let executions = 0;
  const deferred: { resolve?: (value: OfflineCatalogRefreshResult) => void } = {};
  const execute = () => {
    executions += 1;
    return new Promise<OfflineCatalogRefreshResult>((resolve) => {
      deferred.resolve = resolve;
    });
  };
  const first = coordinator.start("2001", execute);
  const second = coordinator.start("2001", execute);
  assert.equal(executions, 1);
  deferred.resolve?.(buildResult("2001", 5));
  assert.equal((await first).metadata.itemCount, 5);
  assert.equal((await second).metadata.itemCount, 5);
  assert.equal(coordinator.isRunning, false);
});

test("切换门店会中止旧门店的下载并启动新门店", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  const states: OfflineCatalogRefreshState[] = [];
  coordinator.subscribe((state) => states.push(state));
  let oldAborted = false;
  const oldRun = coordinator.start("1042", ({ signal }) =>
    new Promise<OfflineCatalogRefreshResult>((_resolve, reject) => {
      signal.addEventListener("abort", () => {
        oldAborted = true;
        reject(new Error("aborted"));
      });
    }),
  );
  // 旧任务的 rejection 必须被消费，否则是未处理拒绝。
  const oldSettled = oldRun.catch(() => "rejected" as const);
  assert.equal(coordinator.isRunning, true);

  const newRun = coordinator.start("2001", async () => buildResult("2001", 7));
  assert.equal(await oldSettled, "rejected");
  assert.equal(oldAborted, true, "旧门店的下载必须收到 abort");
  const result = await newRun;
  assert.equal(result.metadata.storeCode, "2001");
  assert.equal(coordinator.isRunning, false);
  const finalState = states.at(-1);
  assert.equal(finalState?.kind, "success");
  assert.equal(finalState?.kind === "success" ? finalState.storeCode : null, "2001");
});

test("连续切换多个门店时只有最后一个门店落地", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  const started: string[] = [];
  const hang = (storeCode: string) => ({ signal }: { signal: AbortSignal }) => {
    started.push(storeCode);
    return new Promise<OfflineCatalogRefreshResult>((resolve, reject) => {
      signal.addEventListener("abort", () => reject(new Error("aborted")));
      if (storeCode === "3003") {
        resolve(buildResult("3003", 3));
      }
    });
  };
  const a = coordinator.start("1042", hang("1042")).catch(() => undefined);
  const b = coordinator.start("2001", hang("2001")).catch(() => undefined);
  const c = coordinator.start("3003", hang("3003"));
  await a;
  await b;
  assert.equal((await c).metadata.storeCode, "3003");
  // 2001 在排队期间就被 3003 抢占，连下载都不必启动。
  assert.deepEqual(started, ["1042", "3003"], "被抢占且尚未启动的门店不得再启动");
  assert.equal(coordinator.isRunning, false);
});

test("抢占排队期间同店再次请求不叠加，也不会重下", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  const started: string[] = [];
  const release: { resolve?: () => void } = {};
  const oldRun = coordinator
    .start("1042", ({ signal }) => {
      started.push("1042");
      return new Promise<OfflineCatalogRefreshResult>((_resolve, reject) => {
        signal.addEventListener("abort", () => {
          // 旧任务解绕要花时间（真实路径是 discardStaging 按批回收）。
          setTimeout(() => reject(new Error("aborted")), 5);
          release.resolve = () => undefined;
        });
      });
    })
    .catch(() => undefined);

  // 用户切到 2001 并连点两次「立即更新」。
  const first = coordinator.start("2001", async () => {
    started.push("2001");
    return buildResult("2001", 7);
  });
  const second = coordinator.start("2001", async () => {
    started.push("2001-again");
    return buildResult("2001", 7);
  });
  assert.equal(first, second, "排队中的同店请求必须复用同一次下载");

  await oldRun;
  assert.equal((await first).metadata.storeCode, "2001");
  assert.deepEqual(started, ["1042", "2001"], "2001 只能启动一次");
  assert.equal(coordinator.isRunning, false);
  release.resolve?.();
});

test("抢占排队期间第三次调用不会产生取消不掉的孤儿任务", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  const started: string[] = [];
  const aborted: string[] = [];
  const hang = (storeCode: string) => ({ signal }: { signal: AbortSignal }) => {
    started.push(storeCode);
    return new Promise<OfflineCatalogRefreshResult>((resolve, reject) => {
      signal.addEventListener("abort", () => {
        aborted.push(storeCode);
        reject(new Error("aborted"));
      });
      if (storeCode === "3003") {
        resolve(buildResult("3003", 3));
      }
    });
  };
  const a = coordinator.start("1042", hang("1042")).catch(() => undefined);
  const b = coordinator.start("2001", hang("2001")).catch(() => undefined);
  const c = coordinator.start("3003", hang("3003"));
  await a;
  await b;
  assert.equal((await c).metadata.storeCode, "3003");
  // 2001 被 3003 抢占时还没真正启动，所以它绝不能跑起来变成后台孤儿任务。
  assert.deepEqual(started, ["1042", "3003"], "被抢占且尚未启动的门店不得再启动");
  assert.deepEqual(aborted, ["1042"]);
  assert.equal(coordinator.isRunning, false);
});

test("取消后同店再次请求会重新开始，而不是复用注定失败的那次", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  let starts = 0;
  const firstRun = coordinator
    .start("2001", ({ signal }) => {
      starts += 1;
      return new Promise<OfflineCatalogRefreshResult>((_resolve, reject) => {
        signal.addEventListener("abort", () => reject(new Error("aborted")));
      });
    })
    .catch(() => "cancelled" as const);
  coordinator.cancel();
  const retry = coordinator.start("2001", async () => {
    starts += 1;
    return buildResult("2001", 9);
  });
  assert.equal(await firstRun, "cancelled");
  assert.equal((await retry).metadata.itemCount, 9);
  assert.equal(starts, 2, "取消中的任务不得被复用");
});

test("空门店编码直接拒绝且不影响进行中的下载", async () => {
  const coordinator = new OfflineCatalogRefreshCoordinator();
  await assert.rejects(() => coordinator.start("   ", async () => buildResult("x", 0)));
  assert.equal(coordinator.isRunning, false);
});
