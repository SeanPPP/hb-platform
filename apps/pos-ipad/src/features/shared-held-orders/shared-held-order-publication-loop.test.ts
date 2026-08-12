import assert from "node:assert/strict";
import test from "node:test";

import { SharedHeldOrderPublicationLoop } from "./shared-held-order-publication-loop";
import type { SharedHeldOrderPublicationRunResult } from "./shared-held-order-publication-worker";

const emptyResult = Object.freeze({
  evaluatedOrders: 0,
  stagedPendingPublish: 0,
  blocked: 0,
  published: 0,
  failedCapability: 0,
  failedPublish: 0,
}) satisfies SharedHeldOrderPublicationRunResult;

test("发布循环恢复后立即执行、周期单飞，暂停与关闭后不再启动新一轮", async () => {
  let intervalTask: (() => void) | null = null;
  let cancelled = 0;
  let calls = 0;
  let releaseFirst!: () => void;
  const firstRun = new Promise<void>((resolve) => {
    releaseFirst = resolve;
  });
  const loop = new SharedHeldOrderPublicationLoop({
    worker: {
      async runOnce() {
        calls += 1;
        if (calls === 1) await firstRun;
        return emptyResult;
      },
    },
    scheduler: {
      every(intervalMs, task) {
        assert.equal(intervalMs, 10_000);
        intervalTask = task;
        return () => {
          cancelled += 1;
          intervalTask = null;
        };
      },
    },
    intervalMs: 10_000,
  });

  loop.resume();
  assert.equal(calls, 1, "resume 必须立即唤醒发布队列");
  (intervalTask as (() => void) | null)?.();
  (intervalTask as (() => void) | null)?.();
  assert.equal(calls, 1, "并发 tick 必须复用同一轮发布");

  releaseFirst();
  await loop.runNow();
  assert.equal(calls, 1, "显式唤醒也应复用尚未收尾的同一轮");

  loop.pause();
  assert.equal(cancelled, 1);
  assert.equal(intervalTask, null);

  loop.resume();
  await loop.runNow();
  assert.equal(calls, 2);
  await loop.shutdown();
  assert.equal(cancelled, 2);
  assert.throws(() => loop.resume(), /PUBLICATION_LOOP_SHUTDOWN/u);
  await assert.rejects(() => loop.runNow(), /PUBLICATION_LOOP_SHUTDOWN/u);
});

test("后台立即执行失败不会形成未处理拒绝，后续周期仍可重试", async () => {
  let intervalTask: (() => void) | null = null;
  let calls = 0;
  const loop = new SharedHeldOrderPublicationLoop({
    worker: {
      async runOnce() {
        calls += 1;
        if (calls === 1) throw new Error("transient database failure");
        return emptyResult;
      },
    },
    scheduler: {
      every(_intervalMs, task) {
        intervalTask = task;
        return () => {
          intervalTask = null;
        };
      },
    },
  });

  loop.resume();
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(calls, 1);

  (intervalTask as (() => void) | null)?.();
  await loop.runNow();
  assert.equal(calls, 2);
  await loop.shutdown();
});

test("删除屏障会暂停周期、等待在途发布，并在恢复前拒绝启动新一轮", async () => {
  let intervalTask: (() => void) | null = null;
  let calls = 0;
  let releaseRun!: () => void;
  const activeRun = new Promise<void>((resolve) => {
    releaseRun = resolve;
  });
  const loop = new SharedHeldOrderPublicationLoop({
    worker: {
      async runOnce() {
        calls += 1;
        await activeRun;
        return emptyResult;
      },
    },
    scheduler: {
      every(_intervalMs, task) {
        intervalTask = task;
        return () => {
          intervalTask = null;
        };
      },
    },
  });

  loop.resume();
  const barrier = loop.pauseAndWait();
  assert.equal(intervalTask, null);
  await assert.rejects(() => loop.runNow(), /PUBLICATION_LOOP_PAUSED/u);
  assert.equal(calls, 1);

  releaseRun();
  await barrier;
  loop.resume();
  await loop.runNow();
  assert.equal(calls, 2);
  await loop.shutdown();
});
