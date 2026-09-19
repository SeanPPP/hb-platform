import assert from "node:assert/strict";
import {
  buildApplyItems,
  resolvePrintQueue,
  retryPriceUpdatePrintItem,
  runPriceUpdateBatch,
  summarizeApplyResult,
  summarizePrintProgress,
  type PriceUpdateRunDependencies,
  type PriceUpdateRunState,
} from "./batch-runner";
import { PriceLabelPrintBusyError, runPriceLabelPrintExclusive } from "./price-label-print-lock";
import { createTask } from "./test-helpers";
import type { PriceUpdateApplyItem, PriceUpdateBatchResult, StorePriceUpdateTask } from "./types";

function batchResult(items: PriceUpdateBatchResult["items"], patch: Partial<PriceUpdateBatchResult> = {}): PriceUpdateBatchResult {
  return {
    items,
    successCount: items.filter((item) => item.success).length,
    failedCount: items.filter((item) => !item.success).length,
    hqSyncEnabled: false,
    hqSyncSubmittedCount: 0,
    ...patch,
  };
}

function createHarness(options: {
  applyResult?: PriceUpdateBatchResult | Error;
  failPrintFor?: Set<number>;
  failConfirmFor?: Set<number>;
}) {
  const log: string[] = [];
  const applied: PriceUpdateApplyItem[][] = [];
  let printing = 0;
  let maxConcurrentPrints = 0;
  const dependencies: PriceUpdateRunDependencies = {
    async apply(items) {
      applied.push(items);
      log.push(`apply:${items.map((item) => item.taskId).join(",")}`);
      if (options.applyResult instanceof Error) throw options.applyResult;
      return options.applyResult ?? batchResult([]);
    },
    async printLabel(task: StorePriceUpdateTask) {
      printing += 1;
      maxConcurrentPrints = Math.max(maxConcurrentPrints, printing);
      await Promise.resolve();
      printing -= 1;
      if (options.failPrintFor?.has(task.id)) throw new Error("PRINTER_OFFLINE");
      log.push(`print:${task.id}@${task.storeRetailPrice}`);
    },
    async markPrinted(taskId) {
      if (options.failConfirmFor?.has(taskId)) throw new Error("CONFIRM_FAILED");
      log.push(`confirm:${taskId}`);
    },
    runPrintExclusive: runPriceLabelPrintExclusive,
  };
  return { log, applied, dependencies, getMaxConcurrentPrints: () => maxConcurrentPrints };
}

async function main() {
  const priceTask = createTask({ id: 1, kind: "PriceUpdate", storeRetailPrice: 10, targetRetailPrice: 12, targetDiscountRate: null });
  const labelTask = createTask({ id: 2, kind: "LabelOnly", storeRetailPrice: 8 });
  const alignedTask = createTask({ id: 3, kind: "PriceUpdate", targetRetailPrice: 15, targetDiscountRate: 0.2 });
  const changedTask = createTask({ id: 4, kind: "PriceUpdate", targetRetailPrice: 20 });

  assert.deepEqual(
    buildApplyItems([priceTask, labelTask, alignedTask]),
    [
      { taskId: 1, expectedTargetRetailPrice: 12, expectedTargetDiscountRate: null },
      { taskId: 3, expectedTargetRetailPrice: 15, expectedTargetDiscountRate: 0.2 },
    ],
    "只有需改价任务进入改价请求，expected* 原样传列表里看到的目标值"
  );

  const applyResult = batchResult(
    [
      { taskId: 1, success: true, code: "ok", message: null, task: createTask({ id: 1, kind: "LabelOnly", storeRetailPrice: 12 }) },
      { taskId: 3, success: true, code: "ok", message: null, task: createTask({ id: 3, status: "Completed", completionMode: "PriceAligned" }) },
      { taskId: 4, success: false, code: "target_changed", message: "changed", task: null },
    ],
    { hqSyncEnabled: true, hqSyncSubmittedCount: 2 }
  );

  assert.deepEqual(summarizeApplyResult(3, applyResult), { total: 3, success: 2, failed: 1, targetChanged: 1 });
  assert.deepEqual(summarizeApplyResult(2, null), { total: 2, success: 0, failed: 2, targetChanged: 0 });
  assert.deepEqual(
    summarizeApplyResult(3, batchResult([{ taskId: 1, success: true, code: "ok", message: null, task: null }])),
    { total: 3, success: 1, failed: 2, targetChanged: 0 },
    "后端漏回的条目也算失败"
  );

  const queue = resolvePrintQueue([priceTask, labelTask, alignedTask, changedTask], applyResult);
  assert.deepEqual(queue.map((item) => item.taskId), [1, 2], "改价后已完成或改价失败的任务不打印");
  assert.equal(queue[0].task.storeRetailPrice, 12, "打印用改价后回传的最新任务（更新后的价格）");
  assert.deepEqual(
    resolvePrintQueue([priceTask], batchResult([{ taskId: 1, success: true, code: "ok", message: null, task: null }])).map((item) => item.task.id),
    [1],
    "接口未回传最新任务时仍打印，价格由目标值兜底"
  );

  // 更新并打印：改价一次 → 串行逐张打印 → 每张立刻回写
  const happy = createHarness({ applyResult });
  const snapshots: PriceUpdateRunState[] = [];
  const finalState = await runPriceUpdateBatch({
    tasks: [priceTask, labelTask, alignedTask, changedTask],
    mode: "applyAndPrint",
    printerReady: true,
    dependencies: happy.dependencies,
    onChange: (next) => snapshots.push(next),
  });
  assert.deepEqual(happy.log, ["apply:1,3,4", "print:1@12", "confirm:1", "print:2@8", "confirm:2"]);
  assert.equal(happy.applied.length, 1, "改价必须一次性提交");
  assert.equal(happy.getMaxConcurrentPrints(), 1, "打印必须串行");
  assert.equal(finalState.phase, "done");
  assert.deepEqual(finalState.apply, { total: 3, success: 2, failed: 1, targetChanged: 1 });
  assert.deepEqual([finalState.hqSyncEnabled, finalState.hqSyncSubmittedCount], [true, 2]);
  assert.deepEqual(summarizePrintProgress(finalState), { total: 2, processed: 2, printed: 2, failed: 0 });
  assert.deepEqual(
    snapshots.map((state) => state.phase).filter((phase, index, all) => all[index - 1] !== phase),
    ["applying", "printing", "done"]
  );

  // 打印失败不回写、不阻塞后续；已出纸但回写失败要区分
  const partial = createHarness({ applyResult, failPrintFor: new Set([1]), failConfirmFor: new Set([2]) });
  let partialState = await runPriceUpdateBatch({
    tasks: [priceTask, labelTask],
    mode: "applyAndPrint",
    printerReady: true,
    dependencies: partial.dependencies,
    onChange: () => {},
  });
  assert.deepEqual(partial.log, ["apply:1", "print:2@8"], "打印失败的任务不得回写已打印");
  assert.deepEqual(
    partialState.prints.map((item) => [item.taskId, item.status, item.printedAwaitingConfirm, item.error]),
    [
      [1, "failed", false, "PRINTER_OFFLINE"],
      [2, "failed", true, "CONFIRM_FAILED"],
    ]
  );
  assert.deepEqual(summarizePrintProgress(partialState), { total: 2, processed: 2, printed: 0, failed: 2 });

  // 单条重试：已出纸的只补回写，绝不重复打印
  const retry = createHarness({});
  partialState = await retryPriceUpdatePrintItem(partialState, 2, retry.dependencies, () => {});
  assert.deepEqual(retry.log, ["confirm:2"], "已出纸的重试不得再打一张");
  partialState = await retryPriceUpdatePrintItem(partialState, 1, retry.dependencies, () => {});
  assert.deepEqual(retry.log, ["confirm:2", "print:1@12", "confirm:1"]);
  assert.deepEqual(partialState.prints.map((item) => item.status), ["printed", "printed"]);
  const unchanged = await retryPriceUpdatePrintItem(partialState, 1, retry.dependencies, () => {});
  assert.equal(unchanged, partialState, "已打印的条目不可重试");

  // 标签机不可用：只改价
  const noPrinter = createHarness({ applyResult });
  const noPrinterState = await runPriceUpdateBatch({
    tasks: [priceTask, labelTask],
    mode: "applyAndPrint",
    printerReady: false,
    dependencies: noPrinter.dependencies,
    onChange: () => {},
  });
  assert.deepEqual(noPrinter.log, ["apply:1"]);
  assert.equal(noPrinterState.printerUnavailable, true);
  assert.deepEqual(noPrinterState.prints, []);

  // 仅更新：不打印
  const applyOnly = createHarness({ applyResult });
  const applyOnlyState = await runPriceUpdateBatch({
    tasks: [priceTask, labelTask],
    mode: "applyOnly",
    printerReady: true,
    dependencies: applyOnly.dependencies,
    onChange: () => {},
  });
  assert.deepEqual(applyOnly.log, ["apply:1"]);
  assert.equal(applyOnlyState.printerUnavailable, false);

  // 只有待换标签：跳过改价请求
  const labelsOnly = createHarness({});
  const labelsOnlyState = await runPriceUpdateBatch({
    tasks: [labelTask],
    mode: "applyAndPrint",
    printerReady: true,
    dependencies: labelsOnly.dependencies,
    onChange: () => {},
  });
  assert.deepEqual(labelsOnly.log, ["print:2@8", "confirm:2"]);
  assert.equal(labelsOnlyState.apply.total, 0);

  // 改价请求整体失败：需改价任务不打印，原本待换标签的仍可打印
  const applyFailed = createHarness({ applyResult: new Error("NETWORK") });
  const applyFailedState = await runPriceUpdateBatch({
    tasks: [priceTask, labelTask],
    mode: "applyAndPrint",
    printerReady: true,
    dependencies: applyFailed.dependencies,
    onChange: () => {},
  });
  assert.deepEqual(applyFailed.log, ["apply:1", "print:2@8", "confirm:2"]);
  assert.deepEqual(applyFailedState.apply, { total: 1, success: 0, failed: 1, targetChanged: 0, error: "NETWORK" });

  // 停止打印：当前张打完后其余标记为已停止，可稍后重试
  const stopping = createHarness({ applyResult });
  let stop = false;
  const stoppedState = await runPriceUpdateBatch({
    tasks: [priceTask, labelTask],
    mode: "applyAndPrint",
    printerReady: true,
    dependencies: stopping.dependencies,
    onChange: (next) => {
      if (next.prints.some((item) => item.status === "printed")) stop = true;
    },
    shouldStop: () => stop,
  });
  assert.deepEqual(stopping.log, ["apply:1", "print:1@12", "confirm:1"]);
  assert.equal(stoppedState.stopped, true);
  assert.deepEqual(stoppedState.prints.map((item) => item.status), ["printed", "skipped"]);

  // 互斥：标签机被占用时整批标记失败而不是排队
  const busy = createHarness({});
  let release!: () => void;
  const holder = runPriceLabelPrintExclusive(() => new Promise<void>((resolve) => { release = resolve; }));
  await assert.rejects(() => runPriceLabelPrintExclusive(async () => {}), PriceLabelPrintBusyError);
  const busyState = await runPriceUpdateBatch({
    tasks: [labelTask],
    mode: "applyAndPrint",
    printerReady: true,
    dependencies: busy.dependencies,
    onChange: () => {},
  });
  assert.deepEqual(busy.log, []);
  assert.deepEqual(busyState.prints.map((item) => [item.status, item.error]), [["failed", "PRICE_LABEL_PRINT_BUSY"]]);
  release();
  await holder;
  await runPriceLabelPrintExclusive(async () => {});

  console.log("price-updates/batch-runner.test.ts: ok");
}

void main();
