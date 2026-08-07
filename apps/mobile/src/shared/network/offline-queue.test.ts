/**
 * 离线请求队列（offline-queue.ts）单元测试。
 * 使用内存存储端口，验证入队/出队/更新/清空/上限逻辑。
 */
import { test } from "node:test";
import assert from "node:assert/strict";

import {
  DEFAULT_MAX_QUEUE_LENGTH,
  DEFAULT_MAX_RETRIES,
  OfflineRequestQueue,
  type OfflineQueueStorage,
  type QueuedRequest,
} from "./offline-queue";

/** 内存存储：模拟 AsyncStorage 的读写行为。 */
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

test("enqueue 追加到队尾并带默认 retryCount/maxRetries", async () => {
  const storage = createMemoryStorage();
  const queue = new OfflineRequestQueue({
    storage,
    createId: () => "id-1",
  });
  const item = await queue.enqueue({ url: "https://a.test/x", method: "POST", body: "{}" });
  assert.equal(item.id, "id-1");
  assert.equal(item.retryCount, 0);
  assert.equal(item.maxRetries, DEFAULT_MAX_RETRIES);
  assert.equal(await queue.size(), 1);
});

test("dequeue 按 id 移除，未命中返回 false", async () => {
  const storage = createMemoryStorage();
  const queue = new OfflineRequestQueue({ storage, createId: () => "id-1" });
  const item = await queue.enqueue({ url: "https://a.test/x", method: "GET" });
  assert.equal(await queue.dequeue(item.id), true);
  assert.equal(await queue.size(), 0);
  assert.equal(await queue.dequeue(item.id), false);
});

test("markRetry 递增 retryCount，条目不存在返回 null", async () => {
  const storage = createMemoryStorage();
  const queue = new OfflineRequestQueue({ storage, createId: () => "id-1" });
  const item = await queue.enqueue({ url: "https://a.test/x", method: "POST", maxRetries: 3 });
  const updated = await queue.markRetry(item.id);
  assert.equal(updated?.retryCount, 1);
  assert.equal(updated?.maxRetries, 3);
  assert.equal(await queue.markRetry("missing"), null);
});

test("getAll 保持 FIFO 顺序", async () => {
  const storage = createMemoryStorage();
  let seq = 0;
  const queue = new OfflineRequestQueue({
    storage,
    createId: () => `id-${++seq}`,
  });
  await queue.enqueue({ url: "https://a.test/1", method: "GET" });
  await queue.enqueue({ url: "https://a.test/2", method: "GET" });
  const all = await queue.getAll();
  assert.deepEqual(all.map((item) => item.url), ["https://a.test/1", "https://a.test/2"]);
});

test("clear 清空队列", async () => {
  const storage = createMemoryStorage();
  const queue = new OfflineRequestQueue({ storage, createId: () => "id-1" });
  await queue.enqueue({ url: "https://a.test/x", method: "GET" });
  await queue.clear();
  assert.equal(await queue.size(), 0);
});

test("超过队列上限时丢弃最旧条目并触发 onDiscard", async () => {
  const storage = createMemoryStorage();
  const discarded: QueuedRequest[] = [];
  let seq = 0;
  const queue = new OfflineRequestQueue({
    storage,
    createId: () => `id-${++seq}`,
    maxQueueLength: 2,
    onDiscard: (item) => discarded.push(item),
  });
  await queue.enqueue({ url: "https://a.test/1", method: "GET" });
  await queue.enqueue({ url: "https://a.test/2", method: "GET" });
  await queue.enqueue({ url: "https://a.test/3", method: "GET" });
  assert.equal(discarded.length, 1);
  assert.equal(discarded[0].url, "https://a.test/1", "应丢弃最旧条目");
  const all = await queue.getAll();
  assert.deepEqual(all.map((item) => item.url), ["https://a.test/2", "https://a.test/3"]);
  assert.equal(DEFAULT_MAX_QUEUE_LENGTH, 100);
});

test("存储读取失败时按空队列继续，不抛错", async () => {
  const queue = new OfflineRequestQueue({
    storage: {
      async load() {
        throw new Error("storage broken");
      },
      async save() {
        throw new Error("storage broken");
      },
    },
    createId: () => "id-1",
  });
  assert.equal(await queue.size(), 0);
  assert.deepEqual(await queue.getAll(), []);
});
