import {
  createProductDetailRequestCoordinator,
  createProductHqSyncMutationCoordinator,
  getHqSyncDisplayState,
  getHqSyncPollDelayMs,
  getHqSyncStatusFailurePollDelayMs,
  isHqSyncInFlight,
  isProductMaintenanceStoreScopeCurrent,
  isRetryableHqSyncStatusHttpError,
  normalizeHqSyncOperation,
} from "./hq-sync";

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((next) => {
    resolve = next;
  });
  return { promise, resolve };
}

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const pending = normalizeHqSyncOperation({
  OperationId: "operation-1",
  Status: "pending",
  ProductCode: "P001",
  StoreCode: "S01",
  AttemptCount: 0,
  NextAttemptAt: "2026-09-03T01:02:03Z",
  Retryable: true,
  ErrorCode: null,
  Message: "本地已保存，正在更新 HQ",
});

assertDeepEqual(
  pending,
  {
    operationId: "operation-1",
    status: "pending",
    productCode: "P001",
    storeCode: "S01",
    attemptCount: 0,
    nextAttemptAt: "2026-09-03T01:02:03Z",
    retryable: true,
    errorCode: null,
    message: "本地已保存，正在更新 HQ",
  },
  "HQ sync operation normalizes the public DTO"
);

assertEqual(isHqSyncInFlight(pending), true, "pending operation is polled");
assertEqual(
  isHqSyncInFlight(normalizeHqSyncOperation({
    operationId: "operation-2",
    status: "processing",
    productCode: "P002",
    attemptCount: 1,
    retryable: true,
    message: "processing",
  })),
  true,
  "processing operation is polled"
);
assertEqual(
  isHqSyncInFlight(normalizeHqSyncOperation({
    operationId: "operation-3",
    status: "retrying",
    productCode: "P003",
    attemptCount: 2,
    retryable: true,
    message: "retrying",
  })),
  true,
  "retrying operation is polled"
);
assertEqual(
  isHqSyncInFlight(normalizeHqSyncOperation({
    operationId: "operation-4",
    status: "blocked",
    productCode: "P004",
    attemptCount: 3,
    retryable: true,
    errorCode: "UNKNOWN_STORE",
    message: "blocked",
  })),
  false,
  "blocked operation waits for an explicit retry"
);
assertEqual(
  isHqSyncInFlight(normalizeHqSyncOperation({
    operationId: "operation-5",
    status: "superseded",
    productCode: "P005",
    attemptCount: 0,
    retryable: false,
    message: "superseded",
  })),
  false,
  "superseded operation is terminal"
);

assertEqual(
  getHqSyncPollDelayMs({ ...pending!, attemptCount: 0 }),
  1_500,
  "first HQ sync status refresh is prompt"
);
assertEqual(
  getHqSyncPollDelayMs({ ...pending!, status: "retrying", attemptCount: 9, nextAttemptAt: null }),
  10_000,
  "mobile polling is capped while backend retry timing remains authoritative"
);
assertEqual(
  getHqSyncPollDelayMs(
    {
      ...pending!,
      status: "retrying",
      attemptCount: 2,
      nextAttemptAt: "2026-09-03T02:00:00.000Z",
    },
    Date.parse("2026-09-03T01:00:00.000Z")
  ),
  3_600_000,
  "retry polling follows the backend next-attempt time instead of polling every ten seconds"
);

assertEqual(isRetryableHqSyncStatusHttpError(undefined), true, "network failures are retried");
assertEqual(isRetryableHqSyncStatusHttpError(408), true, "HTTP 408 is retried");
assertEqual(isRetryableHqSyncStatusHttpError(429), true, "HTTP 429 is retried");
assertEqual(isRetryableHqSyncStatusHttpError(503), true, "HTTP 5xx is retried");
assertEqual(isRetryableHqSyncStatusHttpError(403), false, "HTTP 403 stops status polling");
assertEqual(isRetryableHqSyncStatusHttpError(404), false, "HTTP 404 stops status polling");
assertEqual(
  getHqSyncStatusFailurePollDelayMs(1),
  3_000,
  "the first consecutive status failure backs off"
);
assertEqual(
  getHqSyncStatusFailurePollDelayMs(20),
  60_000,
  "consecutive status failures are capped at one minute"
);
assertEqual(
  isProductMaintenanceStoreScopeCurrent("s01", "S01"),
  true,
  "分店比较忽略大小写"
);
assertEqual(
  isProductMaintenanceStoreScopeCurrent("S01", "S02"),
  false,
  "旧分店记录不能在新分店范围提交"
);
assertEqual(
  isProductMaintenanceStoreScopeCurrent(null, "S01"),
  false,
  "未知目标分店时保守阻止写入"
);

assertEqual(
  normalizeHqSyncOperation({
    operationId: "operation-invalid",
    status: "unknown",
    productCode: "P006",
  }),
  null,
  "unknown status is rejected"
);

assertDeepEqual(
  getHqSyncDisplayState(pending!),
  {
    visible: true,
    messageKey: "messages.hqSyncPending",
    tone: "info",
    canRetry: false,
  },
  "pending operation uses a compact informational status"
);
assertDeepEqual(
  getHqSyncDisplayState({
    ...pending!,
    status: "blocked",
    retryable: false,
  }),
  {
    visible: true,
    messageKey: "messages.hqSyncBlocked",
    tone: "warning",
    canRetry: false,
  },
  "non-retryable blocked operation does not expose a misleading retry action"
);
assertEqual(
  getHqSyncDisplayState({ ...pending!, status: "blocked", retryable: true }).canRetry,
  true,
  "retryable blocked operation exposes manual retry after data is corrected"
);
assertDeepEqual(
  getHqSyncDisplayState({
    ...pending!,
    status: "blocked",
    retryable: false,
    errorCode: "HQ_SYNC_STATUS_UNAVAILABLE",
  }),
  {
    visible: true,
    messageKey: "messages.hqSyncStatusUnavailable",
    tone: "warning",
    canRetry: false,
  },
  "a permanent status lookup error is shown as terminal and explicit"
);
assertEqual(
  getHqSyncDisplayState({ ...pending!, status: "succeeded" }).visible,
  false,
  "successful operation leaves the compact status area"
);

async function verifyMutationCoordinator() {
  const scope = { productCode: "P001", storeCode: "S01" };
  const coordinator = createProductHqSyncMutationCoordinator(scope);
  const first = coordinator.begin();
  const second = coordinator.begin();
  const firstResponse = deferred<"first">();
  const secondResponse = deferred<"second">();

  // 后发请求先成功后，旧响应不得覆盖当前 HQ operation。
  const applied: string[] = [];
  const firstPromise = firstResponse.promise.then((value) => {
    if (coordinator.succeed(first)) {
      applied.push(value);
    }
  });
  const secondPromise = secondResponse.promise.then((value) => {
    if (coordinator.succeed(second)) {
      applied.push(value);
    }
  });
  secondResponse.resolve("second");
  await secondPromise;
  firstResponse.resolve("first");
  await firstPromise;
  assertDeepEqual(applied, ["second"], "逆序成功只保留最新 mutation 的 HQ 状态");

  // 后发失败仅结束自身，不能阻断仍在途的先发成功。
  const afterFailure = createProductHqSyncMutationCoordinator(scope);
  const earlier = afterFailure.begin();
  const later = afterFailure.begin();
  const earlierResponse = deferred<"earlier">();
  const laterResponse = deferred<"later">();
  const recovered: string[] = [];
  const earlierPromise = earlierResponse.promise.then((value) => {
    if (afterFailure.succeed(earlier)) {
      recovered.push(value);
    }
  });
  const laterPromise = laterResponse.promise.then((value) => {
    afterFailure.fail(later);
    return value;
  });
  laterResponse.resolve("later");
  await laterPromise;
  earlierResponse.resolve("earlier");
  await earlierPromise;
  assertDeepEqual(recovered, ["earlier"], "后发失败不能阻断先发成功的 HQ 状态");

  // 后发请求成功但没有产生 outbox operation 时，只能作为普通成功反馈，
  // 不能推进 HQ operation 的成功序号并吞掉仍在途的先发真实同步任务。
  const afterNoOperation = createProductHqSyncMutationCoordinator(scope);
  const operationMutation = afterNoOperation.begin();
  const noOperationMutation = afterNoOperation.begin();
  const operationResponse = deferred<"operation">();
  const noOperationResponse = deferred<"fallback">();
  const operationApplied: string[] = [];
  const operationPromise = operationResponse.promise.then((value) => {
    if (afterNoOperation.succeed(operationMutation)) {
      operationApplied.push(value);
    }
  });
  const noOperationPromise = noOperationResponse.promise.then((value) => {
    if (afterNoOperation.isCurrent(noOperationMutation)) {
      operationApplied.push(value);
    }
  });
  noOperationResponse.resolve("fallback");
  await noOperationPromise;
  operationResponse.resolve("operation");
  await operationPromise;
  assertDeepEqual(
    operationApplied,
    ["fallback", "operation"],
    "后发无 operation 的普通反馈不能推进序号或吞掉先发真实 HQ operation"
  );

  // 切换商品或分店后，旧 scope 的请求即使晚到也不能重新接管页面。
  const invalidated = createProductHqSyncMutationCoordinator(scope);
  const stale = invalidated.begin();
  invalidated.invalidate({ productCode: "P002", storeCode: "S02" });
  assertEqual(
    invalidated.succeed(stale),
    false,
    "切换商品或分店后旧 scope 的 HQ 响应必须被丢弃"
  );
}

async function verifyDetailRequestCoordinator() {
  const coordinator = createProductDetailRequestCoordinator({ productCode: "A", storeCode: "S01" });
  const first = coordinator.begin({ productCode: "A", storeCode: "S01" });
  const second = coordinator.begin({ productCode: "A", storeCode: "S01" });
  const firstResponse = deferred<"first">();
  const secondResponse = deferred<"second">();
  const applied: string[] = [];

  const firstPromise = firstResponse.promise.then((value) => {
    if (coordinator.isCurrent(first)) {
      applied.push(value);
    }
  });
  const secondPromise = secondResponse.promise.then((value) => {
    if (coordinator.isCurrent(second)) {
      applied.push(value);
    }
  });
  secondResponse.resolve("second");
  await secondPromise;
  firstResponse.resolve("first");
  await firstPromise;
  assertDeepEqual(applied, ["second"], "同一范围的旧详情响应不能覆盖最新请求");

  const stale = coordinator.begin({ productCode: "A", storeCode: "S01" });
  coordinator.activate({ productCode: "B", storeCode: "S02" });
  assertEqual(
    coordinator.isCurrent(stale),
    false,
    "切换商品或分店后旧详情响应不能覆盖新范围"
  );
  assertEqual(
    coordinator.isScopeActive({ productCode: "A", storeCode: "S01" }),
    false,
    "旧范围在切店后不再可应用本地保存的回读结果"
  );
}

void Promise.all([verifyMutationCoordinator(), verifyDetailRequestCoordinator()]);
