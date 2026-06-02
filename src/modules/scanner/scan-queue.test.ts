import assert from "node:assert/strict";
import {
  canCompleteScanJob,
  completeActiveScanJob,
  createInitialScanQueue,
  enqueueScanJob,
  startScanJob,
} from "./scan-queue";

const options = {
  duplicateWindowMs: 250,
  maxSize: 2,
};

function job(barcode: string, scanTraceId = `trace-${barcode}`, storeCode = "1024") {
  return {
    barcode,
    scanTraceId,
    source: "hid" as const,
    storeCode,
  };
}

function run() {
  const first = startScanJob(createInitialScanQueue(), job("8058617603635"), 1_000, options).queue;

  const queued = enqueueScanJob(first, job("8058617586723"), 1_100, options);
  assert.equal(queued.decision.type, "queued", "busy 时不同条码应进入等待队列");
  assert.equal(queued.queue.pending.length, 1, "队列应保留等待处理的条码");

  const duplicate = enqueueScanJob(queued.queue, job("8058617603635", "trace-repeat"), 1_200, options);
  assert.equal(duplicate.decision.type, "duplicate", "短窗口内重复 active 条码应被去重");
  assert.equal(duplicate.queue.pending.length, 1, "重复条码不应扩大队列");

  const duplicateQueued = enqueueScanJob(queued.queue, job("8058617586723", "trace-repeat-queued"), 1_300, options);
  assert.equal(duplicateQueued.decision.type, "duplicate", "短窗口内重复 pending 条码应被去重");

  const overflowBase = enqueueScanJob(queued.queue, job("8058617597194"), 1_400, options).queue;
  const overflow = enqueueScanJob(overflowBase, job("8058617614167"), 1_500, options);
  assert.equal(overflow.decision.type, "overflow", "队列满时应记录溢出决策");
  assert.equal(overflow.queue.pending.length, 2, "队列长度不能超过上限");
  assert.equal(overflow.queue.pending[0]?.barcode, "8058617597194", "溢出时采用丢最旧的保守策略");
  assert.equal(overflow.queue.pending[1]?.barcode, "8058617614167", "最新不同条码应保留等待处理");

  const next = completeActiveScanJob(overflow.queue).queue;
  assert.equal(next.active?.barcode, "8058617597194", "当前处理完成后应自动提升下一条为 active");
  assert.equal(next.pending.length, 1, "提升后队列中只剩后续条码");

  const staleComplete = completeActiveScanJob(overflow.queue, "trace-old-request");
  assert.equal(staleComplete.completed, false, "非当前 active 的旧请求不能完成队列任务");
  assert.equal(staleComplete.queue.active?.scanTraceId, overflow.queue.active?.scanTraceId, "旧请求不能改写当前 active");

  const completed = completeActiveScanJob(first).queue;
  const directDuplicate = startScanJob(completed, job("8058617603635", "trace-direct-repeat"), 1_200, options);
  assert.equal(directDuplicate.decision.type, "duplicate", "处理刚结束后的短窗口重复条码也应被去重");
  assert.equal(directDuplicate.queue.active, null, "直接重复条码不应重新成为 active");

  const afterWindow = startScanJob(completed, job("8058617603635", "trace-direct-after-window"), 1_260, options);
  assert.equal(afterWindow.decision.type, "started", "超过设备抖动窗口后同条码应允许再次扫描");
  assert.equal(afterWindow.queue.active?.storeCode, "1024", "队列任务应保留创建时门店");

  const staleStoreQueue = startScanJob(createInitialScanQueue(), job("2222222222222", "trace-new", "2048"), 2_000, options).queue;
  assert.equal(
    canCompleteScanJob(staleStoreQueue, job("1111111111111", "trace-old", "1024"), 1, 2),
    false,
    "旧门店任务迟到 finally 时不能释放新门店 active 任务"
  );
  assert.equal(
    canCompleteScanJob(staleStoreQueue, staleStoreQueue.active!, 2, 2),
    true,
    "当前 generation 的 active 任务完成时才允许推进队列"
  );

  console.log("scan-queue.test.ts: ok");
}

run();
