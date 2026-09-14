import assert from "node:assert/strict";
import { test } from "node:test";
import {
  createStaffBarcodeBatch,
  hasIncompleteStaffBarcodeConfirmation,
  invalidateStaffBarcodeOperationSession,
  isStaffBarcodeOperationSessionCurrent,
  loadStaffBarcodeBatch,
  processNextStaffBarcodeTarget,
  resolveStaffBarcodeUncertain,
  runStaffBarcodeActionExclusive,
  skipStaffBarcodeTarget,
  updateStaffBarcodeOperationSession,
  type StaffBarcodeBatch,
} from "./controller";
import type { StaffBarcodeQueueStorage } from "./storage";

function createBatch() {
  return createStaffBarcodeBatch({
    actorGuid: "actor-a",
    storeCode: "S001",
    now: () => "2026-09-10T00:00:00.000Z",
    targets: [
      { userGuid: "user-a", employeeName: "A", username: "a", active: true },
      { userGuid: "user-b", employeeName: "B", username: "b", active: true },
    ],
  });
}

test("批量仅接受同一作用域内的有效且不重复员工", () => {
  assert.throws(() => createStaffBarcodeBatch({
    actorGuid: "actor-a", storeCode: "S001",
    targets: [{ userGuid: "user-a", employeeName: "A", username: "a", active: false }],
  }), /INACTIVE/);
  assert.throws(() => createStaffBarcodeBatch({
    actorGuid: "actor-a", storeCode: "S001",
    targets: [
      { userGuid: "user-a", employeeName: "A", username: "a", active: true },
      { userGuid: "user-a", employeeName: "A", username: "a", active: true },
    ],
  }), /INVALID/);
});

test("确认失败只重试确认，不重复出纸", async () => {
  let batch = createBatch();
  let printCalls = 0;
  let confirmCalls = 0;
  const changes: StaffBarcodeBatch[] = [];
  const dependencies = {
    ensureBarcode: async () => ({ exists: true, barcode: "2912345678906" }),
    printLabel: async () => { printCalls += 1; },
    confirmPrint: async () => {
      confirmCalls += 1;
      if (confirmCalls === 1) throw new Error("network");
    },
    createAttemptId: () => "attempt-a",
    runExclusive: async <T>(operation: () => Promise<T>) => operation(),
  };
  batch = await processNextStaffBarcodeTarget(batch, dependencies, async (next) => { changes.push(next); });
  assert.equal(batch.targets[0].status, "failed");
  assert.equal(batch.targets[0].pendingConfirmation?.phase, "printed");
  batch = await processNextStaffBarcodeTarget(batch, dependencies, async () => undefined);
  assert.equal(printCalls, 1);
  assert.equal(confirmCalls, 2);
  assert.equal(batch.targets[0].status, "done");
  assert.equal(batch.targets[1].status, "pending");
  assert.ok(changes.some((change) => change.targets[0].status === "printing"));
});

test("确认接口成功但清理pending首次持久化失败时仍保留printed证据且只补确认", async () => {
  let batch = createBatch();
  batch.targets = batch.targets.slice(0, 1);
  let printCalls = 0;
  let confirmCalls = 0;
  let failedClearOnce = false;
  const dependencies = {
    ensureBarcode: async () => ({ exists: true, barcode: "2912345678906" }),
    printLabel: async () => { printCalls += 1; },
    confirmPrint: async () => { confirmCalls += 1; },
    createAttemptId: () => "attempt-persist-failure",
    runExclusive: async <T>(operation: () => Promise<T>) => operation(),
  };
  const persist = async (next: StaffBarcodeBatch) => {
    if (!failedClearOnce && next.targets[0].pendingConfirmation === null && confirmCalls === 1) {
      failedClearOnce = true;
      throw new Error("secure-store-write-failed");
    }
  };
  batch = await processNextStaffBarcodeTarget(batch, dependencies, persist);
  assert.equal(batch.targets[0].status, "failed");
  assert.equal(batch.targets[0].pendingConfirmation?.phase, "printed");

  batch = await processNextStaffBarcodeTarget(batch, dependencies, async () => undefined);
  assert.equal(batch.targets[0].status, "done");
  assert.equal(printCalls, 1);
  assert.equal(confirmCalls, 2);
});

test("打印结果不确定时暂停，必须由用户确认是否已出纸", async () => {
  let batch = createBatch();
  batch = await processNextStaffBarcodeTarget(batch, {
    ensureBarcode: async () => ({ exists: true, barcode: "2912345678906" }),
    printLabel: async () => { throw new Error("socket lost"); },
    confirmPrint: async () => undefined,
    createAttemptId: () => "attempt-a",
    runExclusive: async <T>(operation: () => Promise<T>) => operation(),
  }, async () => undefined);
  assert.equal(batch.targets[0].status, "uncertain");
  assert.equal(resolveStaffBarcodeUncertain(batch, "user-a", "printed").targets[0].status, "failed");
  assert.equal(resolveStaffBarcodeUncertain(batch, "user-a", "notPrinted").targets[0].status, "pending");
});

test("安全恢复严格校验 actor 与 store，账号切换不恢复他人队列", async () => {
  let raw: string | null = JSON.stringify(createBatch());
  const storage: StaffBarcodeQueueStorage = {
    get: async () => raw,
    set: async (_actor, _store, value) => { raw = value; },
  };
  assert.equal((await loadStaffBarcodeBatch(storage, "actor-a", "S001"))?.targets.length, 2);
  assert.equal(await loadStaffBarcodeBatch(storage, "actor-b", "S001"), null);
  assert.equal(raw, null);
});

test("恢复中断状态时区分未确认出纸与仅待服务端确认", async () => {
  const printing = createBatch();
  printing.targets[0] = {
    ...printing.targets[0],
    status: "printing",
    pendingConfirmation: {
      attemptId: "attempt-a",
      barcode: "2912345678906",
      phase: "printing",
      createdAt: "2026-09-10T00:00:00.000Z",
    },
  };
  let raw: string | null = JSON.stringify(printing);
  const storage: StaffBarcodeQueueStorage = {
    get: async () => raw,
    set: async (_actor, _store, value) => { raw = value; },
  };
  assert.equal((await loadStaffBarcodeBatch(storage, "actor-a", "S001"))?.targets[0].status, "uncertain");
  printing.targets[0].pendingConfirmation = { ...printing.targets[0].pendingConfirmation!, phase: "printed" };
  raw = JSON.stringify(printing);
  assert.equal((await loadStaffBarcodeBatch(storage, "actor-a", "S001"))?.targets[0].status, "failed");
});

test("跳过和结束判断保留已出纸但未确认的 attempt，重新打开仍可补确认", async () => {
  const batch = createBatch();
  batch.targets[0] = {
    ...batch.targets[0],
    status: "failed",
    pendingConfirmation: {
      attemptId: "attempt-printed",
      barcode: "2912345678906",
      phase: "printed",
      createdAt: "2026-09-10T00:00:00.000Z",
    },
  };
  const skipped = skipStaffBarcodeTarget(batch, "user-a");
  assert.equal(skipped.targets[0].status, "skipped");
  assert.equal(skipped.targets[0].pendingConfirmation?.attemptId, "attempt-printed");
  assert.equal(hasIncompleteStaffBarcodeConfirmation(skipped), true);

  let raw: string | null = JSON.stringify(skipped);
  const storage: StaffBarcodeQueueStorage = {
    get: async () => raw,
    set: async (_actor, _store, value) => { raw = value; },
  };
  const restored = await loadStaffBarcodeBatch(storage, "actor-a", "S001");
  assert.equal(restored?.targets[0].status, "failed");
  assert.equal(restored?.targets[0].pendingConfirmation?.attemptId, "attempt-printed");
});

test("作用域变化后停止后续物理打印", async () => {
  let currentScope = true;
  let printCalls = 0;
  const batch = await processNextStaffBarcodeTarget(createBatch(), {
    ensureBarcode: async () => { currentScope = false; return { exists: true, barcode: "2912345678906" }; },
    printLabel: async () => { printCalls += 1; },
    confirmPrint: async () => undefined,
    createAttemptId: () => "attempt-a",
    runExclusive: async <T>(operation: () => Promise<T>) => operation(),
    isScopeCurrent: () => currentScope,
  }, async () => undefined);
  assert.equal(printCalls, 0);
  assert.equal(batch.targets[0].status, "failed");
});

test("每轮打开使用独立generation，关闭重开ABA和卸载都会使旧token失效", () => {
  const opened = { key: "actor-a:S001:user-a", active: true, generation: 1 };
  const firstToken = opened.generation;
  const closed = updateStaffBarcodeOperationSession(opened, opened.key, false);
  const reopened = updateStaffBarcodeOperationSession(closed, opened.key, true);
  assert.equal(isStaffBarcodeOperationSessionCurrent(reopened, firstToken), false);
  assert.equal(isStaffBarcodeOperationSessionCurrent(reopened, reopened.generation), true);

  const unmounted = invalidateStaffBarcodeOperationSession(reopened);
  assert.equal(isStaffBarcodeOperationSessionCurrent(unmounted, reopened.generation), false);
  assert.ok(unmounted.generation > reopened.generation);
});

test("动作CAS覆盖ensure到confirm整链，晚到的第二次点击不会重复出纸", async () => {
  const gate = { inFlight: false };
  let releaseEnsure!: () => void;
  const ensureReady = new Promise<void>((resolve) => { releaseEnsure = resolve; });
  let printCalls = 0;
  const operation = async () => {
    await ensureReady;
    printCalls += 1;
  };
  const first = runStaffBarcodeActionExclusive(gate, operation);
  const second = await runStaffBarcodeActionExclusive(gate, operation);
  assert.deepEqual(second, { started: false });
  releaseEnsure();
  await first;
  assert.equal(printCalls, 1);
  assert.equal(gate.inFlight, false);
});

test("批量运行期间跳过和结束共用CAS，不得覆盖或删除在途凭据", async () => {
  const gate = { inFlight: false };
  let releaseRun!: () => void;
  const running = new Promise<void>((resolve) => { releaseRun = resolve; });
  let skippedWrites = 0;
  let clearedWrites = 0;
  const first = runStaffBarcodeActionExclusive(gate, async () => running);
  const skip = await runStaffBarcodeActionExclusive(gate, async () => { skippedWrites += 1; });
  const finish = await runStaffBarcodeActionExclusive(gate, async () => { clearedWrites += 1; });
  assert.deepEqual(skip, { started: false });
  assert.deepEqual(finish, { started: false });
  assert.equal(skippedWrites, 0);
  assert.equal(clearedWrites, 0);
  releaseRun();
  await first;
});

test("已出纸待确认遇到服务端旧码变化时标记作废且不自动补打", async () => {
  let printCalls = 0;
  let confirmCalls = 0;
  let batch = createBatch();
  batch.targets = batch.targets.slice(0, 1);
  batch = await processNextStaffBarcodeTarget(batch, {
    ensureBarcode: async () => ({ exists: true, barcode: "2912345678906" }),
    printLabel: async () => { printCalls += 1; },
    confirmPrint: async () => {
      confirmCalls += 1;
      const error = new Error("CASHIER_BARCODE_CHANGED") as Error & { code?: string };
      error.code = "CASHIER_BARCODE_CHANGED";
      throw error;
    },
    createAttemptId: () => "attempt-obsolete",
    runExclusive: async <T>(operation: () => Promise<T>) => operation(),
  }, async () => undefined);
  assert.equal(batch.targets[0].status, "obsolete");
  assert.equal(batch.targets[0].pendingConfirmation, null);

  batch = await processNextStaffBarcodeTarget(batch, {
    ensureBarcode: async () => ({ exists: true, barcode: "2912345678907" }),
    printLabel: async () => { printCalls += 1; },
    confirmPrint: async () => { confirmCalls += 1; },
    createAttemptId: () => "attempt-new",
    runExclusive: async <T>(operation: () => Promise<T>) => operation(),
  }, async () => undefined);
  assert.equal(printCalls, 1);
  assert.equal(confirmCalls, 1);
  assert.equal(batch.targets[0].status, "obsolete");
});
