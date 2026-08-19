import test from 'node:test';
import assert from 'node:assert/strict';
import { createBatchQueue } from '../src/lib/batch.js';

function manualScheduler() {
  const scheduled = [];
  return {
    scheduled,
    schedule: (fn) => {
      scheduled.push(fn);
      return scheduled.length - 1;
    },
    cancel: () => {},
    fire: async () => {
      const fns = scheduled.splice(0);
      for (const fn of fns) await fn();
    },
  };
}

test('微批去重：同一 key 只入队一次', async () => {
  const batches = [];
  const sched = manualScheduler();
  const q = createBatchQueue({
    flush: async (entries) => {
      batches.push(entries);
      const out = {};
      for (const e of entries) out[e.key] = `${e.item}x`;
      return out;
    },
    schedule: sched.schedule,
    cancel: sched.cancel,
    delayMs: 150,
  });
  const p1 = q.enqueue('A', 'A');
  const p2 = q.enqueue('A', 'A');
  assert.equal(q.pendingSize(), 1);
  await sched.fire();
  assert.equal(batches.length, 1);
  assert.deepEqual(batches[0].map((e) => e.key), ['A']);
  assert.equal(await p1, 'Ax');
  assert.equal(await p2, 'Ax');
});

test('150ms 微批合并多次 enqueue', async () => {
  const sched = manualScheduler();
  const batches = [];
  const q = createBatchQueue({
    flush: async (entries) => {
      batches.push(entries);
      const out = {};
      for (const e of entries) out[e.key] = e.item;
      return out;
    },
    schedule: sched.schedule,
    cancel: sched.cancel,
    delayMs: 150,
  });
  q.enqueue('a', '1');
  q.enqueue('b', '2');
  q.enqueue('c', '3');
  assert.equal(sched.scheduled.length, 1);
  await sched.fire();
  assert.equal(batches.length, 1);
  assert.equal(batches[0].length, 3);
});

test('每批最多100，超量分批', async () => {
  const batches = [];
  const q = createBatchQueue({
    flush: async (entries) => {
      batches.push(entries);
      const out = {};
      for (const e of entries) out[e.key] = `${e.item}!`;
      return out;
    },
    schedule: () => 1,
    cancel: () => {},
  });
  const promises = [];
  for (let i = 0; i < 250; i++) promises.push(q.enqueue(`k${i}`, `v${i}`));
  await q.flushNow();
  assert.equal(batches.length, 3);
  assert.deepEqual(
    batches.map((b) => b.length),
    [100, 100, 50],
  );
  const values = await Promise.all(promises);
  assert.equal(values[0], 'v0!');
  assert.equal(values[249], 'v249!');
});

test('完成后缓存命中，重复 enqueue 不再 flush', async () => {
  let flushes = 0;
  const q = createBatchQueue({
    flush: async (entries) => {
      flushes++;
      const out = {};
      for (const e of entries) out[e.key] = e.item;
      return out;
    },
    schedule: () => 1,
    cancel: () => {},
    cacheTtlMs: 60000,
  });
  const p1 = q.enqueue('A', 'a');
  await q.flushNow();
  assert.equal(await p1, 'a');
  assert.equal(flushes, 1);
  const p2 = q.enqueue('A', 'a');
  assert.equal(await p2, 'a');
  assert.equal(flushes, 1);
});

test('flush 失败时入队 promise 拒绝', async () => {
  const q = createBatchQueue({
    flush: async () => {
      throw new Error('boom');
    },
    schedule: () => 1,
    cancel: () => {},
  });
  const p = q.enqueue('x', 'x');
  await q.flushNow();
  await assert.rejects(p, /boom/);
});
