// src/pages/Warehouse/StoreOrders/syncJobPolling.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/pages/Warehouse/StoreOrders/syncJobPolling.ts
var STORE_ORDER_SYNC_POLL_INTERVAL_MS = 2e3;
var STORE_ORDER_SYNC_TIMEOUT_MS = 10 * 60 * 1e3;
var StoreOrderSyncPollingTimeoutError = class extends Error {
  constructor() {
    super("store-order-sync-polling-timeout");
    this.name = "StoreOrderSyncPollingTimeoutError";
  }
};
var StoreOrderSyncPollingCancelledError = class extends Error {
  constructor() {
    super("store-order-sync-polling-cancelled");
    this.name = "StoreOrderSyncPollingCancelledError";
  }
};
function isTerminalStatus(status) {
  return status === "Succeeded" || status === "Failed";
}
function createStoreOrderSyncJobPoller({
  jobId,
  getJob,
  pollIntervalMs = STORE_ORDER_SYNC_POLL_INTERVAL_MS,
  timeoutMs = STORE_ORDER_SYNC_TIMEOUT_MS,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout
}) {
  let pollingTimer = null;
  let timeoutTimer = null;
  let stopped = false;
  let rejectPromise = null;
  const clearTimers = () => {
    if (pollingTimer) {
      clearTimeoutFn(pollingTimer);
      pollingTimer = null;
    }
    if (timeoutTimer) {
      clearTimeoutFn(timeoutTimer);
      timeoutTimer = null;
    }
  };
  const promise = new Promise((resolve, reject) => {
    rejectPromise = reject;
    const scheduleNextPoll = () => {
      pollingTimer = setTimeoutFn(async () => {
        try {
          const result = await getJob(jobId);
          if (stopped) {
            return;
          }
          if (isTerminalStatus(result.status)) {
            clearTimers();
            resolve(result);
            return;
          }
          scheduleNextPoll();
        } catch (error) {
          if (stopped) {
            return;
          }
          clearTimers();
          reject(error);
        }
      }, pollIntervalMs);
    };
    scheduleNextPoll();
    timeoutTimer = setTimeoutFn(() => {
      if (stopped) {
        return;
      }
      stopped = true;
      clearTimers();
      reject(new StoreOrderSyncPollingTimeoutError());
    }, timeoutMs);
  });
  const stop = () => {
    if (stopped) {
      return;
    }
    stopped = true;
    clearTimers();
    rejectPromise?.(new StoreOrderSyncPollingCancelledError());
  };
  return {
    promise,
    stop
  };
}

// src/pages/Warehouse/StoreOrders/syncJobPolling.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
async function assertRejects(execute, expectedError, message) {
  try {
    await execute();
  } catch (error) {
    assert(error instanceof expectedError, message);
    return;
  }
  throw new Error(`${message}\u3002Expected promise to reject`);
}
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
function createFakeTimer() {
  let sequence = 0;
  let now = 0;
  const tasks = /* @__PURE__ */ new Map();
  return {
    setTimeout: (callback, delay) => {
      const id = sequence + 1;
      sequence = id;
      tasks.set(id, { id, execute: callback, delay, dueAt: now + delay });
      return id;
    },
    clearTimeout: (id) => {
      if (typeof id === "number") {
        tasks.delete(id);
      }
    },
    flushNext: () => {
      const next = Array.from(tasks.values()).sort((left, right) => {
        if (left.dueAt !== right.dueAt) {
          return left.dueAt - right.dueAt;
        }
        return left.id - right.id;
      })[0];
      if (!next) {
        throw new Error("\u6CA1\u6709\u53EF\u6267\u884C\u7684\u5B9A\u65F6\u4EFB\u52A1");
      }
      tasks.delete(next.id);
      now = next.dueAt;
      next.execute();
      return next.delay;
    },
    pendingCount: () => tasks.size
  };
}
var pageFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/index.tsx");
var pageSource = readFileSync(pageFile, "utf8");
async function main() {
  const failures = [];
  const pageSourceFailure = await runTest("\u9875\u9762\u5E94\u901A\u8FC7 job \u8F6E\u8BE2\u6587\u4EF6\u63A5\u7EBF\u540C\u6B65\u6D41\u7A0B", () => {
    assert(pageSource.includes("createStoreOrderFullHqSyncJob"), "\u9875\u9762\u5E94\u521B\u5EFA\u5168\u91CF HQ \u540C\u6B65\u4EFB\u52A1");
    assert(pageSource.includes("createStoreOrderIncrementalHqSyncJob"), "\u9875\u9762\u5E94\u521B\u5EFA\u589E\u91CF HQ \u540C\u6B65\u4EFB\u52A1");
    assert(pageSource.includes("getStoreOrderHqSyncJob"), "\u9875\u9762\u5E94\u8F6E\u8BE2 HQ \u540C\u6B65\u4EFB\u52A1");
    assert(pageSource.includes("createStoreOrderSyncJobPoller"), "\u9875\u9762\u5E94\u4F7F\u7528\u72EC\u7ACB\u8F6E\u8BE2\u5668");
    assert(pageSource.includes("stopSyncPollingRef.current?.()"), "\u9875\u9762\u5378\u8F7D\u65F6\u5E94\u6E05\u7406\u8F6E\u8BE2\u5B9A\u65F6\u5668");
    assert(pageSource.includes("result.status === 'Failed'"), "\u9875\u9762\u5E94\u5355\u72EC\u5904\u7406\u5931\u8D25\u72B6\u6001");
    assert(pageSource.includes("void loadData()"), "\u540C\u6B65\u6210\u529F\u540E\u5E94\u5237\u65B0\u5F53\u524D\u7B5B\u9009\u5217\u8868");
    assert(pageSource.includes("const [incrementalConflictStrategy, setIncrementalConflictStrategy]"), "\u9875\u9762\u5E94\u7EF4\u62A4\u589E\u91CF\u540C\u6B65\u51B2\u7A81\u7B56\u7565\u72B6\u6001");
    assert(pageSource.includes("const handleOpenIncrementalHqSync = () =>"), "\u9875\u9762\u5E94\u901A\u8FC7\u7EDF\u4E00\u5165\u53E3\u6253\u5F00\u589E\u91CF\u540C\u6B65\u5F39\u7A97");
    assert(pageSource.includes("setIncrementalConflictStrategy(DEFAULT_INCREMENTAL_CONFLICT_STRATEGY)"), "\u6BCF\u6B21\u6253\u5F00\u6216\u53D6\u6D88\u589E\u91CF\u540C\u6B65\u5F39\u7A97\u65F6\u5E94\u6062\u590D\u9ED8\u8BA4\u51B2\u7A81\u7B56\u7565");
    assert(pageSource.includes("onClick={handleOpenIncrementalHqSync}"), "\u589E\u91CF\u540C\u6B65\u6309\u94AE\u5E94\u5148\u91CD\u7F6E\u9ED8\u8BA4\u7B56\u7565\u518D\u6253\u5F00\u5F39\u7A97");
    assert(pageSource.includes("conflictStrategy: incrementalConflictStrategy"), "\u63D0\u4EA4\u589E\u91CF\u540C\u6B65\u65F6\u5E94\u5E26\u4E0A\u5F53\u524D\u51B2\u7A81\u7B56\u7565");
    assert(pageSource.includes("value={incrementalConflictStrategy}"), "\u51B2\u7A81\u5904\u7406\u5355\u9009\u7EC4\u5E94\u7ED1\u5B9A\u5F53\u524D\u7B56\u7565");
    assert(pageSource.includes("t('storeOrders.syncConflictLatestWins')"), "\u9875\u9762\u5E94\u6E32\u67D3\u6309\u6700\u65B0\u66F4\u65B0\u65F6\u95F4\u5904\u7406\u51B2\u7A81\u7684\u6587\u6848");
    assert(pageSource.includes("t('storeOrders.syncConflictHqWins')"), "\u9875\u9762\u5E94\u6E32\u67D3 HQ \u4F18\u5148\u7684\u51B2\u7A81\u5904\u7406\u6587\u6848");
    assert(pageSource.includes("t('storeOrders.syncSkippedSummary'"), "\u540C\u6B65\u6210\u529F\u63D0\u793A\u5E94\u62FC\u63A5\u8DF3\u8FC7\u7EDF\u8BA1");
  });
  if (pageSourceFailure) failures.push(pageSourceFailure);
  const successPollingFailure = await runTest("\u8F6E\u8BE2\u5668\u5E94\u6BCF\u6B21\u7B49\u5F85\u5B9A\u65F6\u5668\u540E\u7EE7\u7EED\u8BF7\u6C42\u76F4\u5230\u6210\u529F", async () => {
    const timer = createFakeTimer();
    const statuses = [
      { jobId: "job-1", status: "Queued", message: "\u6392\u961F\u4E2D" },
      { jobId: "job-1", status: "Running", message: "\u540C\u6B65\u4E2D" },
      {
        jobId: "job-1",
        status: "Succeeded",
        message: "\u540C\u6B65\u5B8C\u6210",
        conflictStrategy: "LatestWins",
        ordersSynced: 3,
        detailsSynced: 5,
        ordersUpdated: 1,
        detailsUpdated: 2,
        skippedOrdersBecauseLocalNewer: 4,
        skippedDetailsBecauseLocalNewer: 6
      }
    ];
    const requestedJobIds = [];
    const poller = createStoreOrderSyncJobPoller({
      jobId: "job-1",
      pollIntervalMs: 2e3,
      timeoutMs: 3e4,
      getJob: async (jobId) => {
        requestedJobIds.push(jobId);
        return statuses.shift();
      },
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    const firstDelay = timer.flushNext();
    await Promise.resolve();
    const secondDelay = timer.flushNext();
    await Promise.resolve();
    const thirdDelay = timer.flushNext();
    await Promise.resolve();
    const result = await poller.promise;
    assertEqual(firstDelay, 2e3, "\u9996\u6B21\u8F6E\u8BE2\u5E94\u7B49\u5F85 2 \u79D2");
    assertEqual(secondDelay, 2e3, "\u540E\u7EED\u8F6E\u8BE2\u4E5F\u5E94\u7B49\u5F85 2 \u79D2");
    assertEqual(thirdDelay, 2e3, "\u6210\u529F\u524D\u7684\u6700\u540E\u4E00\u6B21\u8F6E\u8BE2\u4E5F\u5E94\u7B49\u5F85 2 \u79D2");
    assertDeepEqual(requestedJobIds, ["job-1", "job-1", "job-1"], "\u8F6E\u8BE2\u5E94\u6301\u7EED\u67E5\u8BE2\u540C\u4E00\u4E2A job");
    assertDeepEqual(
      result,
      {
        jobId: "job-1",
        status: "Succeeded",
        message: "\u540C\u6B65\u5B8C\u6210",
        conflictStrategy: "LatestWins",
        ordersSynced: 3,
        detailsSynced: 5,
        ordersUpdated: 1,
        detailsUpdated: 2,
        skippedOrdersBecauseLocalNewer: 4,
        skippedDetailsBecauseLocalNewer: 6
      },
      "\u8F6E\u8BE2\u6210\u529F\u540E\u5E94\u8FD4\u56DE\u6700\u7EC8\u6458\u8981"
    );
  });
  if (successPollingFailure) failures.push(successPollingFailure);
  const failedPollingFailure = await runTest("\u8F6E\u8BE2\u5668\u5E94\u628A Failed \u4F5C\u4E3A\u6700\u7EC8\u72B6\u6001\u8FD4\u56DE\u800C\u4E0D\u662F\u672C\u5730\u62A5\u9519", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderSyncJobPoller({
      jobId: "job-2",
      pollIntervalMs: 2e3,
      timeoutMs: 3e4,
      getJob: async () => ({
        jobId: "job-2",
        status: "Failed",
        message: "\u540E\u7AEF\u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5\u9519\u8BEF"
      }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    const result = await poller.promise;
    assertDeepEqual(
      result,
      {
        jobId: "job-2",
        status: "Failed",
        message: "\u540E\u7AEF\u540C\u6B65\u5931\u8D25\uFF1A\u6D4B\u8BD5\u9519\u8BEF"
      },
      "Failed \u5E94\u4F5C\u4E3A\u540E\u7AEF\u6700\u7EC8\u7ED3\u679C\u900F\u4F20"
    );
  });
  if (failedPollingFailure) failures.push(failedPollingFailure);
  const timeoutFailure = await runTest("\u8F6E\u8BE2\u5668\u8D85\u65F6\u5E94\u629B\u51FA\u672C\u5730 timeout\uFF0C\u800C\u4E0D\u662F\u4F2A\u88C5\u6210\u540E\u7AEF\u5931\u8D25", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderSyncJobPoller({
      jobId: "job-3",
      pollIntervalMs: 2e3,
      timeoutMs: 2e3,
      getJob: async () => ({
        jobId: "job-3",
        status: "Running",
        message: "\u540C\u6B65\u4E2D"
      }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    await assertRejects(
      () => poller.promise,
      StoreOrderSyncPollingTimeoutError,
      "\u8F6E\u8BE2\u8D85\u65F6\u5E94\u629B\u51FA\u672C\u5730\u8D85\u65F6\u9519\u8BEF"
    );
  });
  if (timeoutFailure) failures.push(timeoutFailure);
  const cancelFailure = await runTest("\u505C\u6B62\u8F6E\u8BE2\u5E94\u6E05\u7406\u6302\u8D77\u5B9A\u65F6\u5668\u5E76\u629B\u51FA\u53D6\u6D88\u9519\u8BEF", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderSyncJobPoller({
      jobId: "job-4",
      pollIntervalMs: 2e3,
      timeoutMs: 3e4,
      getJob: async () => ({
        jobId: "job-4",
        status: "Running",
        message: "\u540C\u6B65\u4E2D"
      }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    poller.stop();
    assertEqual(timer.pendingCount(), 0, "\u505C\u6B62\u8F6E\u8BE2\u540E\u4E0D\u5E94\u6B8B\u7559\u5B9A\u65F6\u5668");
    await assertRejects(
      () => poller.promise,
      StoreOrderSyncPollingCancelledError,
      "\u505C\u6B62\u8F6E\u8BE2\u540E\u5E94\u629B\u51FA\u53D6\u6D88\u9519\u8BEF"
    );
  });
  if (cancelFailure) failures.push(cancelFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("syncJobPolling.test: ok");
}
await main();
