/**
 * 网络恢复控制器（network-recovery-controller.ts）单元测试。
 * 注入内存队列、可控的后端探测与发送端口、可记录的定时器端口，
 * 验证"网络恢复 → 后端可达 → 自动补传"的完整链路。
 */
import { test } from "node:test";
import assert from "node:assert/strict";

import {
  NetworkRecoveryController,
  RETRY_BASE_MS,
  RETRY_MAX_MS,
  type SchedulePort,
} from "./network-recovery-controller";
import {
  OfflineRequestQueue,
  type OfflineQueueStorage,
  type QueuedRequest,
} from "./offline-queue";

/** 记录调度而不真实等待的定时器端口。 */
function createScheduler() {
  const scheduled: Array<{
    delayMs: number;
    cancelled: boolean;
    fn: () => void | Promise<void>;
  }> = [];
  const schedule: SchedulePort = (fn, delayMs) => {
    const entry = { delayMs, cancelled: false, fn };
    scheduled.push(entry);
    return {
      cancel: () => {
        entry.cancelled = true;
      },
    };
  };
  return {
    scheduled,
    schedule,
    /** 手动执行最近一次未取消的调度（模拟退避到期），返回其 Promise。 */
    fireLatest(): Promise<void> | void {
      const entry = [...scheduled].reverse().find((item) => !item.cancelled);
      if (!entry) {
        throw new Error("no pending scheduled retry");
      }
      entry.cancelled = true;
      return entry.fn();
    },
  };
}

function createMemoryStorage(): OfflineQueueStorage & { items: QueuedRequest[] } {
  const items: QueuedRequest[] = [];
  return {
    items,
    async load() {
      return [...items];
    },
    async save(next: QueuedRequest[]) {
      items.length = 0;
      items.push(...next);
    },
  };
}

type Harness = {
  controller: NetworkRecoveryController;
  scheduler: ReturnType<typeof createScheduler>;
  backendReachable: { value: boolean };
  sent: QueuedRequest[];
  sendError: Error | null;
  queue: OfflineRequestQueue;
};

function createHarness(options: { failFirst?: boolean } = {}): Harness {
  const scheduler = createScheduler();
  const storage = createMemoryStorage();
  const queue = new OfflineRequestQueue({ storage, createId: () => `q-${Math.random().toString(36).slice(2)}` });
  const backendReachable = { value: true };
  const sent: QueuedRequest[] = [];
  const sendError: Error | null = null;
  const controller = new NetworkRecoveryController({
    queue,
    checkBackend: async () => backendReachable.value,
    send: async (request) => {
      // 先记录尝试，再决定是否失败（failFirst 模拟首条瞬时网络错误）。
      sent.push(request);
      if (sendError) throw sendError;
      if (options.failFirst && sent.length === 1) {
        throw new Error("transient failure");
      }
    },
    schedule: scheduler.schedule,
    nowIso: () => "2026-01-01T00:00:00.000Z",
    onLog: () => undefined,
  });
  return { controller, scheduler, backendReachable, sent, sendError, queue };
}

test("启动时后端不可达：不入队不补传，调度退避重试", async () => {
  const h = createHarness();
  h.backendReachable.value = false;
  await h.controller.start();
  const state = h.controller.getState();
  assert.equal(state.isBackendReachable, false);
  assert.equal(state.isOnline, false);
  assert.equal(h.sent.length, 0);
  assert.equal(h.scheduler.scheduled.length, 1, "不可达时应调度一次退避重试");
  // 退避从基础间隔开始指数增长。
  assert.equal(h.scheduler.scheduled[0].delayMs, RETRY_BASE_MS);
});

test("网络恢复且后端可达：按 FIFO 补传全部请求并出队", async () => {
  const h = createHarness();
  await h.controller.start();
  // 离线期间入队两条请求。
  h.backendReachable.value = false;
  await h.controller.enqueue({ url: "https://a.test/1", method: "POST", body: "{}" });
  await h.controller.enqueue({ url: "https://a.test/2", method: "PUT", body: "{}" });

  // 网络恢复：后端可达，触发补传。
  h.backendReachable.value = true;
  await h.controller.triggerRecovery();

  assert.deepEqual(
    h.sent.map((item) => item.url),
    ["https://a.test/1", "https://a.test/2"],
    "应 FIFO 顺序补传",
  );
  assert.equal(await h.queue.size(), 0, "补传成功后队列应清空");
  const state = h.controller.getState();
  assert.equal(state.pendingCount, 0);
  assert.equal(state.isBackendReachable, true);
  assert.equal(state.isOnline, true);
});

test("补传失败：retryCount 递增，中断本轮并调度退避，恢复后重试成功", async () => {
  const h = createHarness({ failFirst: true });
  await h.controller.start();
  // 离线期间入队（此时已知离线，不会立即补传）。
  h.backendReachable.value = false;
  await h.controller.enqueue({ url: "https://a.test/1", method: "POST", maxRetries: 3 });
  assert.equal(h.sent.length, 0);

  // 网络恢复后补传，首条瞬时失败：retryCount 递增并中断本轮。
  h.backendReachable.value = true;
  await h.controller.triggerRecovery();
  assert.equal(h.sent.length, 1, "首条失败后中断本轮");
  const after = await h.queue.getAll();
  assert.equal(after.length, 1);
  assert.equal(after[0].retryCount, 1, "失败后 retryCount 应 +1");
  assert.ok(h.scheduler.scheduled.length >= 1, "失败后应调度退避");

  // 退避到期（网络已稳定）→ 再次补传成功。
  await h.scheduler.fireLatest();
  assert.equal(h.sent.length, 2);
  assert.equal(await h.queue.size(), 0);
});

test("重试耗尽：条目移出活跃队列", async () => {
  const scheduler = createScheduler();
  const storage = createMemoryStorage();
  const queue = new OfflineRequestQueue({ storage, createId: () => "x" });
  const sent: QueuedRequest[] = [];
  const controller = new NetworkRecoveryController({
    queue,
    checkBackend: async () => true,
    send: async (request) => {
      sent.push(request);
      throw new Error("always fails");
    },
    schedule: scheduler.schedule,
    nowIso: () => "2026-01-01T00:00:00.000Z",
  });
  await controller.start();
  await queue.enqueue({ url: "https://a.test/fail", method: "POST", maxRetries: 1 });

  // 第一轮补传失败 → retryCount=1 → 达到 maxRetries=1 → 移出队列。
  await controller.triggerRecovery();
  assert.equal(sent.length, 1);
  assert.equal(await queue.size(), 0, "超过 maxRetries 的条目应移出活跃队列");
  const state = controller.getState();
  assert.equal(state.pendingCount, 0);
});

test("App 前台恢复触发补传（notifyAppForeground）", async () => {
  const h = createHarness();
  await h.controller.start();
  // 先进入离线，再入队：入队时已知离线，不会立即补传。
  h.backendReachable.value = false;
  await h.controller.enqueue({ url: "https://a.test/1", method: "POST" });
  assert.equal(h.sent.length, 0, "离线入队不应立即补传");

  // 仍离线：前台恢复不补传。
  await h.controller.notifyAppForeground();
  assert.equal(h.sent.length, 0);

  // 网络恢复：前台恢复立即补传。
  h.backendReachable.value = true;
  await h.controller.notifyAppForeground();
  assert.equal(h.sent.length, 1, "前台恢复且后端可达时应立即补传");
  assert.equal(await h.queue.size(), 0);
});

test("停止后不再补传，且退避定时器被取消", async () => {
  const h = createHarness();
  await h.controller.start();
  h.backendReachable.value = false;
  await h.controller.triggerRecovery();
  assert.equal(h.scheduler.scheduled.length, 1);
  h.controller.stop();
  assert.ok(h.scheduler.scheduled[0].cancelled, "stop 应取消待执行的退避调度");
  h.backendReachable.value = true;
  await h.controller.notifyAppForeground();
  assert.equal(h.sent.length, 0, "停止后前台恢复不应补传");
});

test("退避间隔指数增长且封顶 RETRY_MAX_MS", async () => {
  const h = createHarness();
  await h.controller.start();
  h.backendReachable.value = false;
  await h.controller.triggerRecovery();
  assert.equal(h.scheduler.scheduled[0].delayMs, RETRY_BASE_MS);
  await h.controller.triggerRecovery();
  const delays = h.scheduler.scheduled.map((item) => item.delayMs);
  assert.ok(delays[1] > delays[0], "退避间隔应递增");
  assert.ok(Math.max(...delays) <= RETRY_MAX_MS, "退避不应超过上限");
});
