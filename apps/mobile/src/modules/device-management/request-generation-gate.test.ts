import assert from "node:assert/strict";
import test from "node:test";
import { RequestGenerationGate, UnknownCreateGate } from "./request-generation-gate";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((next, fail) => { resolve = next; reject = fail; });
  return { promise, resolve, reject };
}

async function applyWhenCurrent<T>(gate: RequestGenerationGate, generation: number, pending: Promise<T>, writes: T[]) {
  const value = await pending;
  if (gate.isCurrent(generation)) writes.push(value);
}

test("slow POS response cannot overwrite fast Mobile response", async () => {
  const gate = new RequestGenerationGate(); const writes: string[] = [];
  const pos = deferred<string>(); const mobile = deferred<string>();
  void applyWhenCurrent(gate, gate.begin(), pos.promise, writes);
  void applyWhenCurrent(gate, gate.begin(), mobile.promise, writes);
  mobile.resolve("Mobile"); await mobile.promise; pos.resolve("POS"); await pos.promise;
  await Promise.resolve(); assert.deepEqual(writes, ["Mobile"]);
});

for (const event of ["account switch", "permission revoked", "unmount"]) test(`${event} discards an old response`, async () => {
  const gate = new RequestGenerationGate(); const writes: string[] = []; const pending = deferred<string>();
  void applyWhenCurrent(gate, gate.begin(), pending.promise, writes); gate.invalidate(); pending.resolve("stale"); await pending.promise;
  await Promise.resolve(); assert.deepEqual(writes, []);
});

test("旧请求失败不能向新上下文写入错误提示", async () => {
  const gate = new RequestGenerationGate();
  const pending = deferred<string>();
  const requestGeneration = gate.begin();
  const messages: string[] = [];
  const operation = pending.promise.catch(() => {
    if (!gate.isCurrent(requestGeneration)) return;
    messages.push("请求失败");
  });
  gate.invalidate();
  pending.reject(new Error("旧请求超时"));
  await operation;
  assert.deepEqual(messages, []);
});

test("未知结果按稳定上下文保留，切换门店/账号/类型往返不能绕过", () => {
  const gate = new UnknownCreateGate();
  const pos = JSON.stringify(["user-a", "POS"]);
  const mobile = JSON.stringify(["user-a", "Mobile"]);
  const otherAccount = JSON.stringify(["user-b", "POS"]);
  assert.equal(gate.begin(pos), true);
  assert.equal(gate.begin(pos), false);
  assert.equal(gate.isPending(pos), true);
  gate.markUnknown(pos);
  assert.equal(gate.isPending(pos), false);
  assert.equal(gate.canSubmit(mobile), true);
  assert.equal(gate.canSubmit(otherAccount), true);
  assert.equal(gate.canSubmit(pos), false);
  gate.clearForNewContext(mobile);
  assert.equal(gate.canSubmit(pos), false);
  gate.clearForNewContext(pos);
  assert.equal(gate.canSubmit(pos), true);
});

test("旧页面的超时返回仍锁定原门店，只能明确核对该 key 后恢复", async () => {
  const gate = new UnknownCreateGate();
  const pending = deferred<void>();
  const original = JSON.stringify(["user-a", "store-a"]);
  const next = JSON.stringify(["user-a", "store-b"]);
  gate.begin(original);
  const request = pending.promise.catch(() => gate.markUnknown(original));
  assert.equal(gate.canSubmit(next), true);
  pending.reject(new Error("timeout"));
  await request;
  assert.equal(gate.canSubmit(original), false);
  gate.clearForNewContext(next);
  assert.equal(gate.canSubmit(original), false);
});

for (const context of ["activation-user-a-POS", "emergency-user-a-store-a"]) test(`${context}：迟到成功没有显示一次性凭据时仍需核对`, async () => {
  const unknownGate = new UnknownCreateGate();
  const displayGate = new RequestGenerationGate();
  const pending = deferred<string>();
  const displayed: string[] = [];
  unknownGate.begin(context);
  const requestGeneration = displayGate.begin();
  const operation = pending.promise.then((result) => {
    if (!displayGate.isCurrent(requestGeneration)) { unknownGate.markUnknown(context); return; }
    displayed.push(result);
    unknownGate.clearForNewContext(context);
  });
  displayGate.invalidate();
  pending.resolve("synthetic-one-time-result");
  await operation;
  assert.deepEqual(displayed, []);
  assert.equal(unknownGate.canSubmit(context), false);
  assert.equal(unknownGate.isPending(context), false);
});
