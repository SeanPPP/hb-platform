import {
  CashierBarcodePrintConfirmationError,
  executeCashierBarcodePrint,
  isCashierBarcodeChangedError,
  type PendingCashierBarcodePrintConfirmation,
} from "@/modules/employee-profile/cashier-barcode";
import type { StaffBarcodeQueueStorage } from "@/modules/users/staff-barcode/storage";

export type StaffBarcodeBatchStatus = "pending" | "printing" | "done" | "failed" | "uncertain" | "skipped" | "obsolete";

export interface StaffBarcodeBatchTarget {
  userGuid: string;
  employeeName: string;
  username: string;
  status: StaffBarcodeBatchStatus;
  pendingConfirmation: PendingCashierBarcodePrintConfirmation | null;
  error?: string;
}

export interface StaffBarcodeBatch {
  version: 1;
  actorGuid: string;
  storeCode: string;
  createdAt: string;
  targets: StaffBarcodeBatchTarget[];
}

export interface StaffBarcodeOperationSession {
  key: string;
  active: boolean;
  generation: number;
}

export interface StaffBarcodeActionGate {
  inFlight: boolean;
}

export async function runStaffBarcodeActionExclusive<T>(
  gate: StaffBarcodeActionGate,
  operation: () => Promise<T>
): Promise<{ started: false } | { started: true; value: T }> {
  if (gate.inFlight) return { started: false };
  gate.inFlight = true;
  try {
    return { started: true, value: await operation() };
  } finally {
    gate.inFlight = false;
  }
}

export function updateStaffBarcodeOperationSession(
  current: StaffBarcodeOperationSession,
  key: string,
  active: boolean
): StaffBarcodeOperationSession {
  if (current.key === key && current.active === active) return current;
  return { key, active, generation: current.generation + 1 };
}

export function invalidateStaffBarcodeOperationSession(current: StaffBarcodeOperationSession) {
  return { ...current, active: false, generation: current.generation + 1 };
}

export function isStaffBarcodeOperationSessionCurrent(
  current: StaffBarcodeOperationSession,
  generation: number
) {
  return current.active && current.generation === generation;
}

export interface StaffBarcodeBatchDependencies {
  ensureBarcode: (target: StaffBarcodeBatchTarget, storeCode: string) => Promise<{ barcode: string; exists: boolean }>;
  printLabel: (target: StaffBarcodeBatchTarget, barcode: string) => Promise<void>;
  confirmPrint: (
    target: StaffBarcodeBatchTarget,
    storeCode: string,
    pending: PendingCashierBarcodePrintConfirmation
  ) => Promise<unknown>;
  createAttemptId: () => string;
  runExclusive: <T>(operation: () => Promise<T>) => Promise<T>;
  isScopeCurrent?: () => boolean;
}

function normalizeScope(value: string) {
  return value.trim();
}

export function createStaffBarcodeBatch(input: {
  actorGuid: string;
  storeCode: string;
  targets: { userGuid: string; employeeName: string; username: string; active: boolean }[];
  now?: () => string;
}): StaffBarcodeBatch {
  const actorGuid = normalizeScope(input.actorGuid);
  const storeCode = normalizeScope(input.storeCode);
  if (!actorGuid || !storeCode) throw new Error("STAFF_BARCODE_SCOPE_REQUIRED");
  const seen = new Set<string>();
  const targets = input.targets.map((target) => {
    const userGuid = normalizeScope(target.userGuid);
    if (!target.active) throw new Error("STAFF_BARCODE_TARGET_INACTIVE");
    if (!userGuid || seen.has(userGuid)) throw new Error("STAFF_BARCODE_TARGET_INVALID");
    seen.add(userGuid);
    return {
      userGuid,
      employeeName: target.employeeName.trim() || target.username.trim(),
      username: target.username.trim(),
      status: "pending" as const,
      pendingConfirmation: null,
    };
  });
  if (!targets.length) throw new Error("STAFF_BARCODE_TARGETS_REQUIRED");
  return { version: 1, actorGuid, storeCode, createdAt: (input.now ?? (() => new Date().toISOString()))(), targets };
}

export async function saveStaffBarcodeBatch(
  storage: StaffBarcodeQueueStorage,
  batch: StaffBarcodeBatch | null,
  operationScope = "batch"
) {
  if (!batch) return;
  await storage.set(batch.actorGuid, batch.storeCode, JSON.stringify(batch), operationScope);
}

export async function clearStaffBarcodeBatch(
  storage: StaffBarcodeQueueStorage,
  actorGuid: string,
  storeCode: string,
  operationScope = "batch"
) {
  await storage.set(normalizeScope(actorGuid), normalizeScope(storeCode), null, operationScope);
}

export async function loadStaffBarcodeBatch(
  storage: StaffBarcodeQueueStorage,
  actorGuid: string,
  storeCode: string,
  operationScope = "batch"
) {
  const expectedActor = normalizeScope(actorGuid);
  const expectedStore = normalizeScope(storeCode);
  const raw = await storage.get(expectedActor, expectedStore, operationScope);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as StaffBarcodeBatch;
    const valid = parsed.version === 1
      && parsed.actorGuid === expectedActor
      && parsed.storeCode === expectedStore
      && Array.isArray(parsed.targets)
      && parsed.targets.every((target) => target.userGuid && target.username);
    if (valid) {
      const recoveredTargets: StaffBarcodeBatchTarget[] = parsed.targets.map((target) => {
        if (!target.pendingConfirmation || target.status === "done") return target;
        const status: StaffBarcodeBatchStatus = target.pendingConfirmation.phase === "printing" ? "uncertain" : "failed";
        return {
          ...target,
          status,
          error: undefined,
        };
      });
      return {
        ...parsed,
        // 恢复中断任务时，已进入物理打印窗口的记录必须停下来让用户判断；已出纸则只补确认。
        targets: recoveredTargets,
      };
    }
  } catch {
    // 损坏或跨账号记录必须清除，绝不尝试恢复到当前操作者。
  }
  await storage.set(expectedActor, expectedStore, null, operationScope);
  return null;
}

function replaceTarget(batch: StaffBarcodeBatch, userGuid: string, patch: Partial<StaffBarcodeBatchTarget>) {
  return {
    ...batch,
    targets: batch.targets.map((target) => target.userGuid === userGuid ? { ...target, ...patch } : target),
  };
}

export async function processNextStaffBarcodeTarget(
  batch: StaffBarcodeBatch,
  dependencies: StaffBarcodeBatchDependencies,
  onChange: (next: StaffBarcodeBatch) => Promise<void>
) {
  const target = batch.targets.find((item) => item.status === "pending" || item.status === "failed");
  if (!target) return batch;
  let current = replaceTarget(batch, target.userGuid, { status: "printing", error: undefined });
  await onChange(current);
  try {
    const assertScope = () => {
      if (dependencies.isScopeCurrent && !dependencies.isScopeCurrent()) throw new Error("STAFF_BARCODE_SCOPE_CHANGED");
    };
    assertScope();
    const authoritative = target.pendingConfirmation?.barcode
      ? { barcode: target.pendingConfirmation.barcode, exists: true }
      : await dependencies.ensureBarcode(target, batch.storeCode);
    assertScope();
    if (!authoritative.exists || !authoritative.barcode) throw new Error("STAFF_BARCODE_UNAVAILABLE");

    await dependencies.runExclusive(async () => executeCashierBarcodePrint({
      pending: target.pendingConfirmation,
      barcode: authoritative.barcode,
      createAttemptId: dependencies.createAttemptId,
      printLabel: async (barcode) => {
        assertScope();
        await dependencies.printLabel(target, barcode);
        assertScope();
      },
      confirmPrint: async (pending) => {
        assertScope();
        const result = await dependencies.confirmPrint(target, batch.storeCode, pending);
        assertScope();
        return result;
      },
      onPendingChange: async (pendingConfirmation) => {
        const next = replaceTarget(current, target.userGuid, { pendingConfirmation });
        // 只有安全存储成功后才推进内存状态；否则 catch 必须仍看到最后一份已持久化的出纸证据。
        await onChange(next);
        current = next;
      },
    }));
    current = replaceTarget(current, target.userGuid, { status: "done", pendingConfirmation: null });
  } catch (error) {
    let latest = current.targets.find((item) => item.userGuid === target.userGuid)!;
    if (error instanceof CashierBarcodePrintConfirmationError && !latest.pendingConfirmation) {
      current = replaceTarget(current, target.userGuid, {
        pendingConfirmation: error.pendingConfirmation,
      });
      latest = current.targets.find((item) => item.userGuid === target.userGuid)!;
    }
    const changed = isCashierBarcodeChangedError(error)
      || (error instanceof CashierBarcodePrintConfirmationError && isCashierBarcodeChangedError(error.cause));
    if (changed && latest.pendingConfirmation?.phase === "printed") {
      // 服务端已明确旧码失效：保留“作废”结果但清除不可再确认的 attempt，绝不自动打印新码。
      current = replaceTarget(current, target.userGuid, {
        status: "obsolete",
        pendingConfirmation: null,
        error: "STAFF_BARCODE_OBSOLETE",
      });
      await onChange(current);
      return current;
    }
    const confirmationOnly = error instanceof CashierBarcodePrintConfirmationError
      || latest.pendingConfirmation?.phase === "printed";
    current = replaceTarget(current, target.userGuid, {
      status: confirmationOnly ? "failed" : latest.pendingConfirmation?.phase === "printing" ? "uncertain" : "failed",
      error: error instanceof Error ? error.message : String(error),
    });
  }
  await onChange(current);
  return current;
}

export function resolveStaffBarcodeUncertain(
  batch: StaffBarcodeBatch,
  userGuid: string,
  choice: "printed" | "notPrinted"
): StaffBarcodeBatch {
  const target = batch.targets.find((item) => item.userGuid === userGuid);
  if (!target || target.status !== "uncertain" || target.pendingConfirmation?.phase !== "printing") return batch;
  const patch: Partial<StaffBarcodeBatchTarget> = choice === "printed"
    ? { status: "failed", pendingConfirmation: { ...target.pendingConfirmation, phase: "printed" }, error: undefined }
    : { status: "pending", pendingConfirmation: null, error: undefined };
  return replaceTarget(batch, userGuid, patch);
}

export function skipStaffBarcodeTarget(batch: StaffBarcodeBatch, userGuid: string) {
  const target = batch.targets.find((item) => item.userGuid === userGuid);
  if (!target) return batch;
  // 跳过只推进本轮队列；已出纸但未确认的 attempt 必须保留，重新打开时恢复为待确认。
  return replaceTarget(batch, userGuid, {
    status: "skipped",
    pendingConfirmation: target.pendingConfirmation,
    error: undefined,
  });
}

export function hasIncompleteStaffBarcodeConfirmation(batch: StaffBarcodeBatch | null) {
  return Boolean(batch?.targets.some((target) => target.pendingConfirmation !== null));
}
