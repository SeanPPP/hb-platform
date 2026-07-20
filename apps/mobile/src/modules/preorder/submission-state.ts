import type {
  PreorderActivationDetail,
  PreorderDraftItemInput,
  PreorderSubmitResult,
} from "./types";
import { isEditablePreorderOrderStatus } from "./availability";

export interface PreorderSubmissionContext {
  contextKey: string;
  activationGuid: string;
  storeCode: string;
  detail: PreorderActivationDetail;
  items: PreorderDraftItemInput[];
}

/** 提交开始时冻结批次、分店、详情和数量，后续响应不能借用其他页面的可变 ref。 */
export function createPreorderSubmissionContext(
  contextKey: string,
  activationGuid: string,
  storeCode: string,
  detail: PreorderActivationDetail,
  items: PreorderDraftItemInput[]
): PreorderSubmissionContext {
  return {
    contextKey,
    activationGuid,
    storeCode,
    detail: {
      ...detail,
      items: detail.items.map((item) => ({ ...item })),
    },
    items: items.map((item) => ({ ...item })),
  };
}

export function isPreorderSubmissionContextCurrent(
  activeContextKey: string,
  context: PreorderSubmissionContext
) {
  return activeContextKey === context.contextKey;
}

interface PreparePreorderSubmissionOptions {
  cancelScheduledAutosave: () => void;
  suspendAutosaveRef: { current: boolean };
  inFlightSave: Promise<PreorderDraftSaveRequestResult> | null;
  readLatest: () => {
    revision: number;
    items: PreorderDraftItemInput[];
  };
}

export type PreorderDraftSaveRequestResult =
  | { kind: "saved" }
  | { kind: "draft-conflict"; detail: PreorderActivationDetail }
  | { kind: "failed" };

export type PreparePreorderSubmissionResult =
  | { kind: "ready"; revision: number; items: PreorderDraftItemInput[] }
  | { kind: "draft-conflict"; detail: PreorderActivationDetail }
  | { kind: "save-failed" };

/** 提交时停止尚未开始的自动保存；已有 PUT 未成功时必须 fail-closed，禁止继续发 stale POST。 */
export async function preparePreorderSubmission({
  cancelScheduledAutosave,
  suspendAutosaveRef,
  inFlightSave,
  readLatest,
}: PreparePreorderSubmissionOptions): Promise<PreparePreorderSubmissionResult> {
  suspendAutosaveRef.current = true;
  cancelScheduledAutosave();
  if (inFlightSave) {
    const saveResult = await inFlightSave;
    if (saveResult.kind === "draft-conflict") return saveResult;
    if (saveResult.kind === "failed") return { kind: "save-failed" };
  }
  const latest = readLatest();
  return {
    kind: "ready",
    revision: latest.revision,
    items: latest.items.map((item) => ({ ...item })),
  };
}

/** 该 ID 只用于同一次提交请求的服务端链路关联，不参与业务数据计算。 */
export function createPreorderSubmissionId() {
  return `preorder-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;
}

export type PreorderActiveMutationEpoch = number;

let activeMutationEpoch = 0;

/** 每次 POST 成功创建独立 epoch，提交后的 GET 不能借用 POST 前启动的旧请求。 */
export function createPreorderActiveMutationEpoch(): PreorderActiveMutationEpoch {
  activeMutationEpoch += 1;
  return activeMutationEpoch;
}

const activeRefreshes = new Map<string, {
  epoch: PreorderActiveMutationEpoch;
  request: Promise<unknown>;
}>();

/** 同一分店同一 mutation epoch 共用请求；新 epoch 必须开启 fresh lane。 */
export function refreshActivePreordersSingleFlight<T>(
  storeCode: string,
  epoch: PreorderActiveMutationEpoch,
  refresh: () => Promise<T>
): Promise<T> {
  const key = storeCode.trim().toUpperCase();
  const existing = activeRefreshes.get(key);
  if (existing && existing.epoch >= epoch) return existing.request as Promise<T>;

  let request: Promise<T>;
  try {
    request = Promise.resolve(refresh());
  } catch (error) {
    request = Promise.reject(error);
  }
  const entry = { epoch, request };
  activeRefreshes.set(key, entry);
  void request.finally(() => {
    if (activeRefreshes.get(key) === entry) activeRefreshes.delete(key);
  }).catch(() => undefined);
  return request;
}

interface SettleSuccessfulPreorderSubmissionOptions {
  writeTerminalCache: () => void;
  finishSubmitPending: () => void;
  showSuccess: () => void;
  refreshInBackground: () => Promise<unknown>;
}

/** POST 已成功即先锁定终态并解锁 UI；后台刷新失败不能把成功降级成失败。 */
export function settleSuccessfulPreorderSubmission({
  writeTerminalCache,
  finishSubmitPending,
  showSuccess,
  refreshInBackground,
}: SettleSuccessfulPreorderSubmissionOptions) {
  writeTerminalCache();
  finishSubmitPending();
  showSuccess();
  try {
    // 同步启动 refresh 以先把 active cache 置为 fail-closed，但不等待网络完成。
    void refreshInBackground().catch(() => undefined);
  } catch {
    // 后台刷新异常不能影响已经确认的提交成功。
  }
}

/** 提交响应是最终事实，先写成本地只读状态，再单独处理待办批次刷新。 */
export function applySubmitResultToDetail(
  detail: PreorderActivationDetail,
  submitted: PreorderSubmitResult
): PreorderActivationDetail {
  return {
    ...detail,
    orderGuid: submitted.orderGuid,
    orderNo: submitted.orderNo,
    orderStatus: submitted.status,
    draftRevision: submitted.draftRevision,
  };
}

/** 用实际提交 body 覆盖详情数量，避免提交后只读缓存继续显示旧服务器草稿。 */
export function applySubmittedItemsToDetail(
  detail: PreorderActivationDetail,
  submittedItems: PreorderDraftItemInput[]
): PreorderActivationDetail {
  const packCountByItem = new Map(
    submittedItems.map((item) => [
      item.activationItemGuid,
      Math.max(0, Math.trunc(item.packCount)),
    ])
  );
  return {
    ...detail,
    items: detail.items.map((item) => {
      const packCount = packCountByItem.get(item.activationItemGuid);
      if (packCount === undefined) return item;
      return {
        ...item,
        packCount,
        orderedQuantity: packCount * Math.max(1, item.minimumOrderQuantity),
      };
    }),
  };
}

/** 即使页面已切换，A 的响应也只能生成 A 的明确 cache 更新。 */
export function applySubmitResultToContext(
  context: PreorderSubmissionContext,
  submitted: PreorderSubmitResult
) {
  return {
    queryKey: [
      "preorder",
      "activation",
      context.activationGuid,
      context.storeCode,
    ] as const,
    detail: applySubmitResultToDetail(
      applySubmittedItemsToDetail(context.detail, context.items),
      submitted
    ),
  };
}

export type PreorderSubmitFailureResolution =
  | { kind: "submitted"; detail: PreorderActivationDetail }
  | { kind: "draft-conflict"; detail: PreorderActivationDetail }
  | { kind: "unresolved" };

/** 提交失败后读取一次服务器事实；Draft 冲突交给用户协调，终态则直接确认已响应。 */
export async function reconcilePreorderSubmitFailure(
  readDetail: () => Promise<PreorderActivationDetail>
): Promise<PreorderSubmitFailureResolution> {
  try {
    const detail = await readDetail();
    if (!isEditablePreorderOrderStatus(detail.orderStatus)) {
      return { kind: "submitted", detail };
    }
    if (detail.orderStatus === "Draft" || detail.orderStatus === "ReturnedForRevision") {
      return { kind: "draft-conflict", detail };
    }
  } catch {
    // 对账失败时保留原始提交错误，不能猜测提交结果。
  }
  return { kind: "unresolved" };
}
