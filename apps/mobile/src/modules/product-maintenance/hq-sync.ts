import type {
  ProductHqSyncOperation,
  ProductHqSyncStatus,
} from "@/modules/product-maintenance/types";

const HQ_SYNC_STATUSES = new Set<ProductHqSyncStatus>([
  "pending",
  "processing",
  "retrying",
  "succeeded",
  "blocked",
  "superseded",
]);

export interface ProductHqSyncMutationScope {
  productCode?: string | null;
  storeCode?: string | null;
}

export interface ProductHqSyncMutationToken {
  readonly generation: number;
  readonly sequence: number;
  readonly scopeKey: string;
  readonly scope: ProductHqSyncMutationScope;
}

export interface ProductHqSyncMutationCoordinator {
  begin(): ProductHqSyncMutationToken;
  activate(scope: ProductHqSyncMutationScope): boolean;
  /**
   * 不会写入成功序号的只读校验。没有 outbox operation 的普通保存提示
   * 必须用它判断当前性，不能调用 succeed() 抢占仍在途的 HQ 操作。
   */
  isCurrent(token: ProductHqSyncMutationToken): boolean;
  succeed(token: ProductHqSyncMutationToken, resultScope?: ProductHqSyncMutationScope): boolean;
  fail(token: ProductHqSyncMutationToken): void;
  invalidate(nextScope?: ProductHqSyncMutationScope): void;
}

function normalizeScopePart(value: string | null | undefined): string {
  return value?.trim().toUpperCase() ?? "";
}

function getMutationScopeKey(scope: ProductHqSyncMutationScope | undefined): string {
  return `${normalizeScopePart(scope?.productCode)}\u0000${normalizeScopePart(scope?.storeCode)}`;
}

/**
 * 全局分店会由其他 tab 改写。写入前必须同时确认详情/目标行属于当前分店，
 * 不能把 S01 的 UUID 带着 S02 的同步范围提交出去。
 */
export function isProductMaintenanceStoreScopeCurrent(
  recordStoreCode: string | null | undefined,
  selectedStoreCode: string | null | undefined
): boolean {
  const record = normalizeScopePart(recordStoreCode);
  const selected = normalizeScopePart(selectedStoreCode);
  return Boolean(record) && Boolean(selected) && record === selected;
}

/**
 * 商品维护 mutation 会并发完成。只让同一页面范围内最新成功的请求更新 HQ 状态，
 * 后发失败不会推进成功序号，因而不会吞掉仍在途的先发成功结果。
 */
export function createProductHqSyncMutationCoordinator(
  initialScope?: ProductHqSyncMutationScope
): ProductHqSyncMutationCoordinator {
  let generation = 0;
  let nextSequence = 0;
  let latestSuccessfulSequence = 0;
  let activeScopeKey = getMutationScopeKey(initialScope);
  let activeScope = initialScope ?? {};

  return {
    begin() {
      nextSequence += 1;
      return {
        generation,
        sequence: nextSequence,
        scopeKey: activeScopeKey,
        scope: activeScope,
      };
    },
    activate(scope) {
      const nextScopeKey = getMutationScopeKey(scope);
      if (nextScopeKey === activeScopeKey) {
        return false;
      }

      generation += 1;
      latestSuccessfulSequence = 0;
      activeScopeKey = nextScopeKey;
      activeScope = scope;
      return true;
    },
    isCurrent(token) {
      return (
        token.generation === generation
        && token.scopeKey === activeScopeKey
        && token.sequence >= latestSuccessfulSequence
      );
    },
    succeed(token, resultScope) {
      if (
        token.generation !== generation
        || token.scopeKey !== activeScopeKey
        || token.sequence < latestSuccessfulSequence
      ) {
        return false;
      }

      latestSuccessfulSequence = token.sequence;
      if (resultScope) {
        // 创建商品会从当前查询范围跳转到新商品；接纳该成功结果后再原子切换范围，
        // 后续详情加载不会误把刚保存的 operation 当作旧请求清除。
        activeScopeKey = getMutationScopeKey(resultScope);
        activeScope = resultScope;
      }
      return true;
    },
    fail() {
      // 失败不能改变 latestSuccessfulSequence，避免后发失败压制先发成功。
    },
    invalidate(nextScope) {
      generation += 1;
      latestSuccessfulSequence = 0;
      activeScopeKey = getMutationScopeKey(nextScope);
      activeScope = nextScope ?? {};
    },
  };
}

export interface ProductDetailRequestToken {
  readonly generation: number;
  readonly sequence: number;
  readonly scopeKey: string;
}

export interface ProductDetailRequestCoordinator {
  begin(scope: ProductHqSyncMutationScope): ProductDetailRequestToken;
  activate(scope: ProductHqSyncMutationScope): boolean;
  invalidate(scope?: ProductHqSyncMutationScope): void;
  isCurrent(token: ProductDetailRequestToken): boolean;
  isScopeActive(scope: ProductHqSyncMutationScope): boolean;
}

/**
 * 商品详情、条码分页与保存后的回读会交错返回。该协调器把“页面范围”与“同范围最新请求”
 * 分开校验，防止 S01/A 的慢响应覆盖已经切换到 S02/B 的页面。
 */
export function createProductDetailRequestCoordinator(
  initialScope?: ProductHqSyncMutationScope
): ProductDetailRequestCoordinator {
  let generation = 0;
  let latestSequence = 0;
  let activeScopeKey = getMutationScopeKey(initialScope);

  const activate = (scope: ProductHqSyncMutationScope) => {
    const nextScopeKey = getMutationScopeKey(scope);
    if (nextScopeKey === activeScopeKey) {
      return false;
    }

    generation += 1;
    latestSequence = 0;
    activeScopeKey = nextScopeKey;
    return true;
  };

  return {
    begin(scope) {
      activate(scope);
      latestSequence += 1;
      return { generation, sequence: latestSequence, scopeKey: activeScopeKey };
    },
    activate,
    invalidate(scope) {
      generation += 1;
      latestSequence = 0;
      activeScopeKey = getMutationScopeKey(scope);
    },
    isCurrent(token) {
      return (
        token.generation === generation
        && token.scopeKey === activeScopeKey
        && token.sequence === latestSequence
      );
    },
    isScopeActive(scope) {
      return getMutationScopeKey(scope) === activeScopeKey;
    },
  };
}

function optionalString(value: unknown): string | null {
  if (value == null) {
    return null;
  }

  const normalized = String(value).trim();
  return normalized || null;
}

export function normalizeHqSyncOperation(payload: unknown): ProductHqSyncOperation | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const data = payload as Record<string, unknown>;
  const operationId = optionalString(data.operationId ?? data.OperationId);
  const productCode = optionalString(data.productCode ?? data.ProductCode);
  const rawStatus = optionalString(data.status ?? data.Status)?.toLowerCase();

  if (!operationId || !productCode || !rawStatus || !HQ_SYNC_STATUSES.has(rawStatus as ProductHqSyncStatus)) {
    return null;
  }

  const rawAttemptCount = Number(data.attemptCount ?? data.AttemptCount ?? 0);
  const attemptCount = Number.isFinite(rawAttemptCount)
    ? Math.max(0, Math.trunc(rawAttemptCount))
    : 0;

  return {
    operationId,
    status: rawStatus as ProductHqSyncStatus,
    productCode,
    storeCode: optionalString(data.storeCode ?? data.StoreCode),
    attemptCount,
    nextAttemptAt: optionalString(data.nextAttemptAt ?? data.NextAttemptAt),
    retryable: Boolean(data.retryable ?? data.Retryable),
    errorCode: optionalString(data.errorCode ?? data.ErrorCode),
    message: optionalString(data.message ?? data.Message) ?? "",
  };
}

export function isHqSyncInFlight(
  operation: ProductHqSyncOperation | null | undefined
): operation is ProductHqSyncOperation {
  return operation != null && (
    operation.status === "pending"
    || operation.status === "processing"
    || operation.status === "retrying"
  );
}

export function getHqSyncPollDelayMs(
  operation: ProductHqSyncOperation,
  nowMs = Date.now()
): number {
  const attemptCount = Math.max(0, operation.attemptCount);
  const routineDelayMs = Math.min(10_000, 1_500 * 2 ** Math.min(attemptCount, 3));
  if (operation.status !== "retrying" || !operation.nextAttemptAt) {
    return routineDelayMs;
  }

  const nextAttemptAtMs = Date.parse(operation.nextAttemptAt);
  if (!Number.isFinite(nextAttemptAtMs) || nextAttemptAtMs <= nowMs) {
    return routineDelayMs;
  }

  // 后端退避最长一小时；客户端直接跟随该时间，避免等待期间每十秒空轮询。
  return Math.max(routineDelayMs, Math.min(3_600_000, nextAttemptAtMs - nowMs));
}

export function isRetryableHqSyncStatusHttpError(status: number | undefined): boolean {
  return status == null || status === 408 || status === 429 || status >= 500;
}

export function getHqSyncStatusFailurePollDelayMs(consecutiveFailureCount: number): number {
  const failures = Math.max(1, Math.trunc(consecutiveFailureCount));
  return Math.min(60_000, 1_500 * 2 ** Math.min(failures, 6));
}

export interface ProductHqSyncDisplayState {
  visible: boolean;
  messageKey:
    | "messages.hqSyncPending"
    | "messages.hqSyncRetrying"
    | "messages.hqSyncBlocked"
    | "messages.hqSyncStatusUnavailable"
    | "messages.hqSyncSucceeded";
  tone: "info" | "warning";
  canRetry: boolean;
}

export function getHqSyncDisplayState(
  operation: ProductHqSyncOperation
): ProductHqSyncDisplayState {
  if (operation.status === "blocked") {
    return {
      visible: true,
      messageKey: operation.errorCode === "HQ_SYNC_STATUS_UNAVAILABLE"
        ? "messages.hqSyncStatusUnavailable"
        : "messages.hqSyncBlocked",
      tone: "warning",
      canRetry: operation.retryable,
    };
  }

  if (operation.status === "retrying") {
    return {
      visible: true,
      messageKey: "messages.hqSyncRetrying",
      tone: "info",
      canRetry: false,
    };
  }

  if (operation.status === "pending" || operation.status === "processing") {
    return {
      visible: true,
      messageKey: "messages.hqSyncPending",
      tone: "info",
      canRetry: false,
    };
  }

  return {
    visible: false,
    messageKey: "messages.hqSyncSucceeded",
    tone: "info",
    canRetry: false,
  };
}
