import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

export type ReturnReceiptKind =
  | "none"
  | "refund-voucher"
  | "refund-receipt";

export type StoredReturnFulfilmentPlan = Readonly<{
  actionId: string;
  returnOrderGuid: string;
  printJobId: string | null;
  drawerEventId: string | null;
  receiptKind: ReturnReceiptKind;
  printReceipt: boolean;
  drawerRequired: boolean;
  materializedAtIso: string | null;
  createdAtIso: string;
}>;

export type MaterializeReturnFulfilmentInput = Readonly<{
  actionId: string;
  expectedReturnOrderGuid: string;
  expectedPrintJobId: string | null;
  expectedDrawerEventId: string | null;
  printerId: string;
  receiptBytes: Uint8Array | null;
  drawerReason: string | null;
}>;

type ReturnFulfilmentPlanRow = Readonly<{
  action_id: unknown;
  return_order_guid: unknown;
  print_job_id: unknown;
  drawer_event_id: unknown;
  receipt_kind: unknown;
  print_receipt: unknown;
  drawer_required: unknown;
  materialized_at_iso: unknown;
  created_at_iso: unknown;
  action_state?: unknown;
  order_state?: unknown;
}>;

/**
 * 退货完成事务只冻结履约身份；receipt domain 随后提供打印机和预渲染字节。
 * 本 facade 将计划、打印任务和钱箱任务一次性物化，崩溃时不会留下半个硬件动作。
 */
export class SqliteReturnFulfilmentPlanStore {
  public constructor(
    private readonly connection: SqliteConnectionPort,
    private readonly encryptor: SensitivePayloadEncryptor,
    private readonly nowIso: () => string,
  ) {}

  public async get(
    actionIdInput: string,
  ): Promise<StoredReturnFulfilmentPlan | null> {
    const actionId = strictText(actionIdInput, "return action id", 128);
    const row = await this.connection.getFirst<ReturnFulfilmentPlanRow>(
      `SELECT action_id, return_order_guid, print_job_id, drawer_event_id,
        receipt_kind, print_receipt, drawer_required,
        materialized_at_iso, created_at_iso
       FROM return_fulfilment_plans
       WHERE action_id = ?`,
      [actionId],
    );
    return row ? mapPlan(row) : null;
  }

  public async listPending(
    limitInput = 50,
  ): Promise<readonly StoredReturnFulfilmentPlan[]> {
    const limit = positiveLimit(limitInput);
    const rows = await this.connection.getAll<ReturnFulfilmentPlanRow>(
      `SELECT plan.action_id, plan.return_order_guid, plan.print_job_id,
        plan.drawer_event_id, plan.receipt_kind, plan.print_receipt,
        plan.drawer_required, plan.materialized_at_iso, plan.created_at_iso,
        action.state AS action_state, local_order.state AS order_state
       FROM return_fulfilment_plans plan
       INNER JOIN return_actions action
         ON action.action_id = plan.action_id
       INNER JOIN local_orders local_order
         ON local_order.order_guid = plan.return_order_guid
       WHERE plan.materialized_at_iso IS NULL
         AND action.state = 'completed'
         AND local_order.state IN (
           'PendingSync', 'Syncing', 'Synced', 'Blocked403', 'Rejected'
         )
       ORDER BY plan.created_at_iso, plan.action_id
       LIMIT ?`,
      [limit],
    );
    return Object.freeze(rows.map(mapPlan));
  }

  public async materialize(
    input: MaterializeReturnFulfilmentInput,
  ): Promise<StoredReturnFulfilmentPlan> {
    const normalized = normalizeMaterialization(input);
    const preflight = await this.get(normalized.actionId);
    if (!preflight) throw new Error("Return fulfilment plan is missing.");
    // 不可信调用参数不能决定是否触发加密；必须先以数据库冻结策略为准。
    assertPlanMatchesMaterialization(preflight, normalized);
    const encodedReceipt =
      normalized.receiptBytes === null
        ? null
        : encodeReceipt(normalized.receiptBytes);
    // drawer-only 计划不得生成、加密或持久化任何伪造小票。
    const encryptedReceipt =
      encodedReceipt === null ||
      !preflight.printReceipt ||
      preflight.materializedAtIso !== null
        ? null
        : await this.encryptor.encrypt(encodedReceipt);
    const materializedAtIso = canonicalIso(
      this.nowIso(),
      "return fulfilment materialized time",
    );

    return this.connection.withExclusiveTransaction(async (transaction) => {
      const row = await transaction.getFirst<ReturnFulfilmentPlanRow>(
        `SELECT plan.action_id, plan.return_order_guid, plan.print_job_id,
          plan.drawer_event_id, plan.receipt_kind, plan.print_receipt,
          plan.drawer_required, plan.materialized_at_iso, plan.created_at_iso,
          action.state AS action_state, local_order.state AS order_state
         FROM return_fulfilment_plans plan
         INNER JOIN return_actions action
           ON action.action_id = plan.action_id
         INNER JOIN local_orders local_order
           ON local_order.order_guid = plan.return_order_guid
         WHERE plan.action_id = ?`,
        [normalized.actionId],
      );
      if (!row) throw new Error("Return fulfilment plan is missing.");
      const plan = mapPlan(row);
      assertPlanMatchesMaterialization(plan, normalized);
      assertMaterializableState(row);

      if (plan.materializedAtIso !== null) {
        await assertMaterializedFacts(
          transaction,
          this.encryptor,
          plan,
          normalized,
          encodedReceipt,
        );
        return plan;
      }

      if (plan.printReceipt) {
        if (plan.printJobId === null || encryptedReceipt === null) {
          throw new Error("Return print materialization is incomplete.");
        }
        await transaction.run(
          `INSERT INTO print_jobs (
            job_id, order_guid, state, printer_id, receipt_ciphertext,
            is_reprint, retry_count, last_error_code, created_at_iso,
            updated_at_iso
          ) VALUES (?, ?, 'Queued', ?, ?, 0, 0, NULL, ?, ?)`,
          [
            plan.printJobId,
            plan.returnOrderGuid,
            normalized.printerId,
            encryptedReceipt,
            materializedAtIso,
            materializedAtIso,
          ],
        );
      }
      if (plan.drawerRequired) {
        await transaction.run(
          `INSERT INTO drawer_events (
            event_id, order_guid, printer_id, print_job_id, state, reason,
            retry_count, requested_at_iso, completed_at_iso, last_error_code,
            created_at_iso, updated_at_iso
          ) VALUES (?, ?, ?, ?, 'Required', ?, 0, NULL, NULL, NULL, ?, ?)`,
          [
            plan.drawerEventId,
            plan.returnOrderGuid,
            normalized.printerId,
            plan.printJobId,
            normalized.drawerReason,
            materializedAtIso,
            materializedAtIso,
          ],
        );
      }
      const changed = await transaction.run(
        `UPDATE return_fulfilment_plans
         SET materialized_at_iso = ?
         WHERE action_id = ? AND return_order_guid = ?
           AND materialized_at_iso IS NULL`,
        [materializedAtIso, plan.actionId, plan.returnOrderGuid],
      );
      if (changed.changes !== 1) {
        throw new Error("Return fulfilment materialization CAS failed.");
      }
      return Object.freeze({
        ...plan,
        materializedAtIso,
      });
    });
  }
}

type NormalizedMaterialization = Readonly<{
  actionId: string;
  expectedReturnOrderGuid: string;
  expectedPrintJobId: string | null;
  expectedDrawerEventId: string | null;
  printerId: string;
  receiptBytes: Uint8Array | null;
  drawerReason: string | null;
}>;

function normalizeMaterialization(
  input: MaterializeReturnFulfilmentInput,
): NormalizedMaterialization {
  if (
    input.receiptBytes !== null &&
    !(input.receiptBytes instanceof Uint8Array)
  ) {
    throw new TypeError("Return receipt bytes are invalid.");
  }
  if (
    input.receiptBytes instanceof Uint8Array &&
    input.receiptBytes.byteLength === 0
  ) {
    throw new TypeError("Return receipt bytes are required.");
  }
  const expectedPrintJobId =
    input.expectedPrintJobId === null
      ? null
      : strictText(
          input.expectedPrintJobId,
          "return print job id",
          128,
        );
  const receiptBytes =
    input.receiptBytes === null
      ? null
      : Uint8Array.from(input.receiptBytes);
  if ((expectedPrintJobId === null) !== (receiptBytes === null)) {
    throw new TypeError("Return print materialization is inconsistent.");
  }
  const expectedDrawerEventId =
    input.expectedDrawerEventId === null
      ? null
      : strictText(
          input.expectedDrawerEventId,
          "return drawer event id",
          128,
        );
  const drawerReason =
    input.drawerReason === null
      ? null
      : strictText(input.drawerReason, "return drawer reason", 256);
  if ((expectedDrawerEventId === null) !== (drawerReason === null)) {
    throw new TypeError("Return drawer materialization is inconsistent.");
  }
  return Object.freeze({
    actionId: strictText(input.actionId, "return action id", 128),
    expectedReturnOrderGuid: strictText(
      input.expectedReturnOrderGuid,
      "return order guid",
      128,
    ),
    expectedPrintJobId,
    expectedDrawerEventId,
    printerId: strictText(input.printerId, "return printer id", 128),
    receiptBytes,
    drawerReason,
  });
}

function mapPlan(row: ReturnFulfilmentPlanRow): StoredReturnFulfilmentPlan {
  const drawerRequired = booleanInteger(
    row.drawer_required,
    "return drawer required",
  );
  const drawerEventId = nullableText(row.drawer_event_id);
  if (drawerRequired !== (drawerEventId !== null)) {
    throw new Error("Persisted return drawer plan is inconsistent.");
  }
  const receiptKind = returnReceiptKind(row.receipt_kind);
  const printReceipt = booleanInteger(
    row.print_receipt,
    "return print receipt",
  );
  const printJobId = nullableText(row.print_job_id);
  const expectsPrint = receiptKind !== "none";
  if (
    printReceipt !== expectsPrint ||
    (printJobId !== null) !== expectsPrint
  ) {
    throw new Error("Persisted return receipt plan is inconsistent.");
  }
  return Object.freeze({
    actionId: text(row.action_id, "return action id"),
    returnOrderGuid: text(row.return_order_guid, "return order guid"),
    printJobId,
    drawerEventId,
    receiptKind,
    printReceipt,
    drawerRequired,
    materializedAtIso: nullableIso(
      row.materialized_at_iso,
      "return fulfilment materialized time",
    ),
    createdAtIso: canonicalIso(
      text(row.created_at_iso, "return fulfilment created time"),
      "return fulfilment created time",
    ),
  });
}

function assertPlanMatchesMaterialization(
  plan: StoredReturnFulfilmentPlan,
  input: NormalizedMaterialization,
): void {
  if (
    plan.returnOrderGuid !== input.expectedReturnOrderGuid ||
    plan.printJobId !== input.expectedPrintJobId ||
    plan.drawerEventId !== input.expectedDrawerEventId ||
    plan.printReceipt !== (input.receiptBytes !== null) ||
    plan.drawerRequired !== (input.drawerReason !== null)
  ) {
    throw new Error("Return fulfilment plan identity has diverged.");
  }
}

function assertMaterializableState(row: ReturnFulfilmentPlanRow): void {
  if (text(row.action_state, "return action state") !== "completed") {
    throw new Error("Return fulfilment requires a completed action.");
  }
  const orderState = text(row.order_state, "return order state");
  if (
    !["PendingSync", "Syncing", "Synced", "Blocked403", "Rejected"].includes(
      orderState,
    )
  ) {
    throw new Error("Return fulfilment order is not durably completed.");
  }
}

async function assertMaterializedFacts(
  connection: SqliteConnectionPort,
  encryptor: SensitivePayloadEncryptor,
  plan: StoredReturnFulfilmentPlan,
  input: NormalizedMaterialization,
  encodedReceipt: string | null,
): Promise<void> {
  if (!plan.printReceipt) {
    const unexpectedPrint = await connection.getFirst<{ job_id: unknown }>(
      `SELECT job_id
       FROM print_jobs
       WHERE order_guid = ? AND is_reprint = 0
       LIMIT 1`,
      [plan.returnOrderGuid],
    );
    if (unexpectedPrint || encodedReceipt !== null) {
      throw new Error("Unexpected return print job was materialized.");
    }
  } else {
    if (plan.printJobId === null || encodedReceipt === null) {
      throw new Error("Materialized return print plan is incomplete.");
    }
    const print = await connection.getFirst<{
      order_guid: unknown;
      printer_id: unknown;
      receipt_ciphertext: unknown;
      is_reprint: unknown;
    }>(
      `SELECT order_guid, printer_id, receipt_ciphertext, is_reprint
       FROM print_jobs
       WHERE job_id = ?`,
      [plan.printJobId],
    );
    if (
      !print ||
      text(print.order_guid, "return print order guid") !==
        plan.returnOrderGuid ||
      text(print.printer_id, "return print printer id") !== input.printerId ||
      booleanInteger(print.is_reprint, "return print reprint flag")
    ) {
      throw new Error("Materialized return print job identity has diverged.");
    }
    const receiptCiphertext = ciphertext(print.receipt_ciphertext);
    if ((await encryptor.decrypt(receiptCiphertext)) !== encodedReceipt) {
      throw new Error("Materialized return receipt bytes have diverged.");
    }
  }

  const drawer =
    plan.drawerEventId === null
      ? null
      : await connection.getFirst<{
          order_guid: unknown;
          printer_id: unknown;
          print_job_id: unknown;
          reason: unknown;
        }>(
          `SELECT order_guid, printer_id, print_job_id, reason
           FROM drawer_events
           WHERE event_id = ?`,
          [plan.drawerEventId],
        );
  if (!plan.drawerRequired) {
    if (drawer) {
      throw new Error("Unexpected return drawer event was materialized.");
    }
    return;
  }
  if (
    !drawer ||
    text(drawer.order_guid, "return drawer order guid") !==
      plan.returnOrderGuid ||
    text(drawer.printer_id, "return drawer printer id") !== input.printerId ||
    nullableText(drawer.print_job_id) !==
      plan.printJobId ||
    text(drawer.reason, "return drawer reason") !== input.drawerReason
  ) {
    throw new Error("Materialized return drawer event identity has diverged.");
  }
}

function encodeReceipt(bytes: Uint8Array): string {
  return JSON.stringify(Array.from(bytes));
}

function ciphertext(value: unknown): Uint8Array {
  if (!(value instanceof Uint8Array) || value.byteLength === 0) {
    throw new Error("Invalid return receipt ciphertext.");
  }
  return value;
}

function strictText(value: unknown, label: string, max: number): string {
  const normalized = text(value, label).trim();
  if (normalized.length === 0 || normalized.length > max) {
    throw new TypeError(`Invalid ${label}.`);
  }
  return normalized;
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string") throw new TypeError(`Invalid ${label}.`);
  return value;
}

function nullableText(value: unknown): string | null {
  return value === null || value === undefined
    ? null
    : text(value, "nullable return fulfilment text");
}

function booleanInteger(value: unknown, label: string): boolean {
  if (value === 0 || value === false) return false;
  if (value === 1 || value === true) return true;
  throw new Error(`Invalid ${label}.`);
}

function returnReceiptKind(value: unknown): ReturnReceiptKind {
  if (
    value === "none" ||
    value === "refund-voucher" ||
    value === "refund-receipt"
  ) {
    return value;
  }
  throw new Error("Invalid return receipt kind.");
}

function canonicalIso(value: string, label: string): string {
  const normalized = strictText(value, label, 64);
  const milliseconds = Date.parse(normalized);
  if (!Number.isFinite(milliseconds)) throw new TypeError(`Invalid ${label}.`);
  return new Date(milliseconds).toISOString();
}

function nullableIso(value: unknown, label: string): string | null {
  return value === null || value === undefined
    ? null
    : canonicalIso(text(value, label), label);
}

function positiveLimit(value: number): number {
  if (!Number.isSafeInteger(value) || value < 1 || value > 100) {
    throw new TypeError("Return fulfilment list limit is invalid.");
  }
  return value;
}
