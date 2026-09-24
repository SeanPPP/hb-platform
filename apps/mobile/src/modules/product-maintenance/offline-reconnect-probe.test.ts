import assert from "node:assert/strict";
import test from "node:test";
import { createOfflineReconnectProbe } from "./offline-reconnect-probe";

interface ScheduledTask {
  fn: () => void;
  delayMs: number;
  cancelled: boolean;
}

function createHarness(results: boolean[]) {
  const scheduled: ScheduledTask[] = [];
  let checks = 0;
  let clock = 10_000;
  const reachableAt: number[] = [];
  const probe = createOfflineReconnectProbe({
    intervalMs: 5_000,
    now: () => clock,
    checkBackend: async () => {
      checks += 1;
      return results.shift() ?? false;
    },
    onReachable: (at) => reachableAt.push(at),
    schedule: (fn, delayMs) => {
      const task: ScheduledTask = { fn, delayMs, cancelled: false };
      scheduled.push(task);
      return { cancel: () => { task.cancelled = true; } };
    },
  });
  const flush = async () => {
    // 让 in-flight 探测的 Promise 链全部落定。
    for (let i = 0; i < 5; i += 1) {
      await Promise.resolve();
    }
  };
  const fireNextTimer = async () => {
    const task = scheduled.find((item) => !item.cancelled);
    assert.ok(task, "应当存在待触发的定时器");
    task.cancelled = true;
    clock += task.delayMs;
    task.fn();
    await flush();
  };
  return { probe, scheduled, flush, fireNextTimer, checks: () => checks, reachableAt, setClock: (v: number) => { clock = v; } };
}

test("启动立即探测，失败后按固定间隔继续", async () => {
  const h = createHarness([false, false, true]);
  h.probe.start();
  await h.flush();
  assert.equal(h.checks(), 1);
  assert.equal(h.scheduled.filter((t) => !t.cancelled).length, 1);
  assert.equal(h.scheduled[0]?.delayMs, 5_000);
  await h.fireNextTimer();
  assert.equal(h.checks(), 2);
  await h.fireNextTimer();
  assert.equal(h.checks(), 3);
  assert.deepEqual(h.reachableAt, [20_000]);
  assert.equal(h.probe.running, false, "成功后必须自动停止");
  assert.equal(h.scheduled.filter((t) => !t.cancelled).length, 0, "成功后不得再排定定时器");
});

test("探测单飞：进行中再次 probeNow 不叠加", async () => {
  // 用对象持有 resolve，避免 TS 控制流把闭包内赋值的局部变量收窄成 never。
  const deferred: { resolve?: (value: boolean) => void } = {};
  let checks = 0;
  const probe = createOfflineReconnectProbe({
    checkBackend: () => {
      checks += 1;
      return new Promise<boolean>((resolve) => { deferred.resolve = resolve; });
    },
    onReachable: () => undefined,
    schedule: () => ({ cancel: () => undefined }),
  });
  probe.start();
  void probe.probeNow();
  void probe.probeNow();
  assert.equal(checks, 1);
  deferred.resolve?.(false);
  await Promise.resolve();
  await Promise.resolve();
  // 第二次探测会把 deferred.resolve 覆写成它自己的 resolve，必须先拿到这个
  // Promise、再放行，否则 await 永远挂住（CI 的 tsx lane 没有 --test-timeout，
  // 会把整个分片卡到 workflow 超时）。
  const second = probe.probeNow();
  assert.equal(checks, 2, "上一次落定后才允许新的即时探测");
  deferred.resolve?.(false);
  await second;
  probe.stop();
});

test("stop 后迟到的成功结果不再触发回调", async () => {
  const deferred: { resolve?: (value: boolean) => void } = {};
  let reached = 0;
  const probe = createOfflineReconnectProbe({
    checkBackend: () => new Promise<boolean>((resolve) => { deferred.resolve = resolve; }),
    onReachable: () => { reached += 1; },
    schedule: () => ({ cancel: () => undefined }),
  });
  probe.start();
  probe.stop();
  deferred.resolve?.(true);
  for (let i = 0; i < 5; i += 1) {
    await Promise.resolve();
  }
  assert.equal(reached, 0);
  assert.equal(probe.running, false);
});

test("未启动时 probeNow 是空操作", async () => {
  let checks = 0;
  const probe = createOfflineReconnectProbe({
    checkBackend: async () => { checks += 1; return true; },
    onReachable: () => undefined,
  });
  await probe.probeNow();
  assert.equal(checks, 0);
});
