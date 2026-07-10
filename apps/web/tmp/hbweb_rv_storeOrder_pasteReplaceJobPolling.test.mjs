// src/pages/Warehouse/StoreOrders/pasteReplaceJobPolling.ts
var StoreOrderPasteReplacePollingTimeoutError = class extends Error {
  constructor() {
    super("Excel \u7C98\u8D34\u5BFC\u5165\u4EFB\u52A1\u8F6E\u8BE2\u8D85\u65F6");
    this.name = "StoreOrderPasteReplacePollingTimeoutError";
  }
};
var StoreOrderPasteReplacePollingCancelledError = class extends Error {
  constructor() {
    super("Excel \u7C98\u8D34\u5BFC\u5165\u4EFB\u52A1\u8F6E\u8BE2\u5DF2\u53D6\u6D88");
    this.name = "StoreOrderPasteReplacePollingCancelledError";
  }
};
function isTerminalStatus(status) {
  return status === "Succeeded" || status === "Failed";
}
function createStoreOrderPasteReplaceJobPoller({
  jobId,
  getJob,
  pollIntervalMs = 2e3,
  timeoutMs = 10 * 60 * 1e3,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout
}) {
  let pollingTimer = null;
  let timeoutTimer = null;
  let stopped = false;
  let rejectPromise = null;
  const clearTimers = () => {
    if (pollingTimer !== null) {
      clearTimeoutFn(pollingTimer);
      pollingTimer = null;
    }
    if (timeoutTimer !== null) {
      clearTimeoutFn(timeoutTimer);
      timeoutTimer = null;
    }
  };
  const promise = new Promise((resolve, reject) => {
    rejectPromise = reject;
    const scheduleNextPoll = () => {
      pollingTimer = setTimeoutFn(() => {
        void poll();
      }, pollIntervalMs);
    };
    const poll = async () => {
      if (stopped) {
        return;
      }
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
    };
    scheduleNextPoll();
    timeoutTimer = setTimeoutFn(() => {
      if (stopped) {
        return;
      }
      stopped = true;
      clearTimers();
      reject(new StoreOrderPasteReplacePollingTimeoutError());
    }, timeoutMs);
  });
  const stop = () => {
    if (stopped) {
      return;
    }
    stopped = true;
    clearTimers();
    rejectPromise?.(new StoreOrderPasteReplacePollingCancelledError());
  };
  return {
    promise,
    stop
  };
}

// src/pages/Warehouse/StoreOrders/pasteReplaceJobPolling.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
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
async function main() {
  const failures = [];
  const successFailure = await runTest("\u8F6E\u8BE2\u5668\u5E94\u6301\u7EED\u67E5\u8BE2\u76F4\u5230 Excel \u7C98\u8D34\u5BFC\u5165\u6210\u529F", async () => {
    const timer = createFakeTimer();
    const statuses = [
      { jobId: "job-1", status: "Queued", message: "\u5DF2\u63D0\u4EA4" },
      { jobId: "job-1", status: "Running", message: "\u5BFC\u5165\u4E2D" },
      { jobId: "job-1", status: "Succeeded", message: "\u5BFC\u5165\u5B8C\u6210", importedCount: 2, skippedCount: 1 }
    ];
    const requestedJobIds = [];
    const poller = createStoreOrderPasteReplaceJobPoller({
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
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    const result = await poller.promise;
    assertDeepEqual(requestedJobIds, ["job-1", "job-1", "job-1"], "\u8F6E\u8BE2\u5E94\u6301\u7EED\u67E5\u8BE2\u540C\u4E00\u4E2A job");
    assertDeepEqual(result, { jobId: "job-1", status: "Succeeded", message: "\u5BFC\u5165\u5B8C\u6210", importedCount: 2, skippedCount: 1 }, "\u8F6E\u8BE2\u5E94\u8FD4\u56DE\u6210\u529F\u7EC8\u6001");
    assert(timer.pendingCount() === 0, "\u6210\u529F\u540E\u5E94\u6E05\u7406\u6240\u6709\u5B9A\u65F6\u5668");
  });
  if (successFailure) failures.push(successFailure);
  const failedFailure = await runTest("\u8F6E\u8BE2\u5668\u9047\u5230\u5931\u8D25\u7EC8\u6001\u4E5F\u5E94\u8FD4\u56DE\u7ED3\u679C\u4EA4\u7ED9\u9875\u9762\u5C55\u793A", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: "job-failed",
      pollIntervalMs: 2e3,
      timeoutMs: 3e4,
      getJob: async () => ({ jobId: "job-failed", status: "Failed", message: "\u5BFC\u5165\u5931\u8D25" }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    const result = await poller.promise;
    assertDeepEqual(result, { jobId: "job-failed", status: "Failed", message: "\u5BFC\u5165\u5931\u8D25" }, "\u5931\u8D25\u7EC8\u6001\u5E94\u8FD4\u56DE\u7ED9\u8C03\u7528\u65B9");
    assert(timer.pendingCount() === 0, "\u5931\u8D25\u7EC8\u6001\u540E\u5E94\u6E05\u7406\u5B9A\u65F6\u5668");
  });
  if (failedFailure) failures.push(failedFailure);
  const timeoutFailure = await runTest("\u8F6E\u8BE2\u5668\u8D85\u65F6\u5E94\u629B\u51FA\u4E13\u7528\u9519\u8BEF", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: "job-timeout",
      pollIntervalMs: 2e3,
      timeoutMs: 3e3,
      getJob: async () => ({ jobId: "job-timeout", status: "Running" }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingTimeoutError, "\u8D85\u65F6\u5E94\u629B\u51FA timeout \u9519\u8BEF");
    assert(timer.pendingCount() === 0, "\u8D85\u65F6\u540E\u5E94\u6E05\u7406\u5B9A\u65F6\u5668");
  });
  if (timeoutFailure) failures.push(timeoutFailure);
  const hangingTimeoutFailure = await runTest("\u8F6E\u8BE2\u5668\u5E94\u5728\u67E5\u8BE2\u6302\u8D77\u65F6\u4ECD\u6309\u72EC\u7ACB\u65F6\u949F\u8D85\u65F6", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: "job-hanging",
      pollIntervalMs: 1e3,
      timeoutMs: 3e3,
      getJob: async () => new Promise(() => {
      }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    await Promise.resolve();
    timer.flushNext();
    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingTimeoutError, "\u67E5\u8BE2\u6302\u8D77\u65F6\u4E5F\u5E94\u6309 wall-clock \u8D85\u65F6");
    assert(timer.pendingCount() === 0, "\u6302\u8D77\u8D85\u65F6\u540E\u5E94\u6E05\u7406\u6240\u6709\u5B9A\u65F6\u5668");
  });
  if (hangingTimeoutFailure) failures.push(hangingTimeoutFailure);
  const cancelFailure = await runTest("\u8F6E\u8BE2\u5668\u505C\u6B62\u540E\u5E94\u629B\u51FA\u53D6\u6D88\u9519\u8BEF\u5E76\u6E05\u7406\u5B9A\u65F6\u5668", async () => {
    const timer = createFakeTimer();
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: "job-cancel",
      pollIntervalMs: 2e3,
      timeoutMs: 3e4,
      getJob: async () => ({ jobId: "job-cancel", status: "Running" }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    poller.stop();
    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingCancelledError, "\u505C\u6B62\u8F6E\u8BE2\u5E94\u629B\u51FA cancel \u9519\u8BEF");
    assert(timer.pendingCount() === 0, "\u53D6\u6D88\u540E\u5E94\u6E05\u7406\u5B9A\u65F6\u5668");
  });
  if (cancelFailure) failures.push(cancelFailure);
  const inflightCancelFailure = await runTest("\u8F6E\u8BE2\u5668\u505C\u6B62\u540E\u8FDB\u884C\u4E2D\u7684\u67E5\u8BE2\u8FD4\u56DE\u4E5F\u4E0D\u5E94\u91CD\u65B0\u6392\u961F", async () => {
    const timer = createFakeTimer();
    let resolveJob = () => {
    };
    const poller = createStoreOrderPasteReplaceJobPoller({
      jobId: "job-inflight-cancel",
      pollIntervalMs: 2e3,
      timeoutMs: 3e4,
      getJob: async () => new Promise((resolve) => {
        resolveJob = resolve;
      }),
      setTimeoutFn: timer.setTimeout,
      clearTimeoutFn: timer.clearTimeout
    });
    timer.flushNext();
    poller.stop();
    await assertRejects(() => poller.promise, StoreOrderPasteReplacePollingCancelledError, "\u8FDB\u884C\u4E2D\u67E5\u8BE2\u505C\u6B62\u540E\u5E94\u629B\u51FA cancel \u9519\u8BEF");
    resolveJob({ jobId: "job-inflight-cancel", status: "Running" });
    await Promise.resolve();
    assert(timer.pendingCount() === 0, "\u505C\u6B62\u540E\u7684\u67E5\u8BE2\u8FD4\u56DE\u4E0D\u5E94\u91CD\u65B0\u6392\u961F");
  });
  if (inflightCancelFailure) failures.push(inflightCancelFailure);
  if (failures.length) {
    throw new Error(failures.join("\n"));
  }
  console.log("pasteReplaceJobPolling.test: ok");
}
main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
