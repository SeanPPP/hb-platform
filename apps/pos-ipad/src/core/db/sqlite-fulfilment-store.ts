import type { DrawerEventState, PrintJobState } from "../contracts/state-machines";

import type { SensitivePayloadEncryptor } from "./sqlite-repositories";
import type { SqliteConnectionPort } from "./types";

export type FulfilmentAuditEvent = Readonly<{
  eventId: string;
  eventType: string;
  occurredAtIso: string;
  orderGuid: string | null;
  correlationId: string;
  payload: Readonly<Record<string, string | number | null>>;
}>;

export type PersistedFulfilmentAuthorization = Readonly<{
  actionId: string;
  permissionCode: string;
  authorizationMode: "current-cashier" | "offline-cache" | "online";
  requestingCashierId: string;
  authorizingCashierId: string | null;
}>;

export type FulfilmentInitialAuthorization = Readonly<{
  context: PersistedFulfilmentAuthorization;
  audit: FulfilmentAuditEvent;
}>;

export type PersistedPrintJobInput = Readonly<{
  jobId: string;
  orderGuid: string;
  printerId: string;
  /** 已由 receipt domain 预渲染完成；这里绝不拼接或猜测重打标记。 */
  receiptBytes: Uint8Array;
  isReprint: boolean;
}>;

export type PersistedDrawerEventInput = Readonly<{
  eventId: string;
  orderGuid: string;
  printerId: string;
  printJobId: string | null;
  reason: string;
}>;

export type StoredFulfilmentPrintJob = Readonly<{
  jobId: string;
  orderGuid: string;
  printerId: string;
  isReprint: boolean;
  bytes: Uint8Array;
  state: PrintJobState;
  retryCount: number;
  authorization?: PersistedFulfilmentAuthorization;
}>;

export type StoredFulfilmentDrawerEvent = Readonly<{
  eventId: string;
  orderGuid: string | null;
  printerId: string;
  state: DrawerEventState;
  reason: string;
  retryCount: number;
}>;

export type ManualDrawerOpenInput = Readonly<{
  /** 与销售授权动作共用稳定 ID；同一 ID 不得再次发出钱箱脉冲。 */
  eventId: string;
  /** 由组合根读取并冻结的持久打印机，UI 不得提供。 */
  printerId: string;
  reason: "MANUAL";
}>;

export type ManualDrawerOpenBeginResult =
  | Readonly<{
      kind: "created";
      event: StoredFulfilmentDrawerEvent;
    }>
  | Readonly<{
      kind: "existing";
      event: StoredFulfilmentDrawerEvent;
    }>;

export type SqliteFulfilmentStoreOptions = Readonly<{
  encryptor: SensitivePayloadEncryptor;
  nowIso: () => string;
  createPrintJobId: () => string;
}>;

/**
 * 履约的 SQLCipher 持久化 facade。
 *
 * 自动恢复永远只领取 Queued/Required；Sending、Ambiguous、Requested、Unknown 都需要人工对账，
 * 因而杀进程或蓝牙超时不会变成盲目重打或重复开箱。
 */
export class SqliteFulfilmentStore {
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly options: SqliteFulfilmentStoreOptions,
  ) {}

  /** 打印和钱箱工作在一个事务内入队；订单账本同事务接线需等待冻结 DatabaseTransactionPort 扩展。 */
  public async enqueueCashFulfilment(input: Readonly<{
    print: PersistedPrintJobInput | null;
    drawer: PersistedDrawerEventInput | null;
  }>): Promise<void> {
    const encryptedReceipt = input.print === null
      ? null
      : await this.options.encryptor.encrypt(encodeReceipt(input.print.receiptBytes));
    await this.db.withExclusiveTransaction(async (tx) => {
      if (input.print && encryptedReceipt) await insertPrintJob(tx, input.print, encryptedReceipt, this.options.nowIso());
      if (input.drawer) await insertDrawerEvent(tx, input.drawer, this.options.nowIso());
    });
  }

  public async enqueuePrintJob(input: PersistedPrintJobInput): Promise<void> {
    const encryptedReceipt = await this.options.encryptor.encrypt(encodeReceipt(input.receiptBytes));
    await this.db.withExclusiveTransaction((tx) => insertPrintJob(tx, input, encryptedReceipt, this.options.nowIso()));
  }

  public enqueueDrawerEvent(input: PersistedDrawerEventInput): Promise<void> {
    return this.db.withExclusiveTransaction((tx) => insertDrawerEvent(tx, input, this.options.nowIso()));
  }

  /**
   * 手动开箱不进入自动 Required 队列。事件先直接持久化为 Requested，
   * 这样崩溃或超时后只能人工对账，启动 drain 不会延迟补发脉冲。
   */
  public beginManualDrawerOpen(
    input: ManualDrawerOpenInput,
    authorization: FulfilmentInitialAuthorization,
  ): Promise<ManualDrawerOpenBeginResult> {
    assertFulfilmentId(input.eventId, "Manual drawer action id");
    assertPrinterId(input.printerId);
    if (input.reason !== "MANUAL") {
      throw new Error("Manual drawer reason must be MANUAL.");
    }
    assertInitialAuthorization(
      authorization,
      {
        eventType: "CASH_DRAWER_OPEN",
        orderGuid: null,
        taskId: input.eventId,
        printerId: input.printerId,
        action: "open-cash-drawer",
        reason: "MANUAL",
        permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
      },
    );
    const now = this.options.nowIso();
    return this.db.withExclusiveTransaction(async (tx) => {
      const existing = await tx.getFirst<DrawerRow>(
        "SELECT * FROM drawer_events WHERE event_id = ?",
        [input.eventId],
      );
      if (existing) {
        const event = assertMatchingManualDrawerEvent(
          existing,
          input,
        );
        const persistedAuthorization =
          await loadInitialAuthorization(
            tx,
            {
              eventType: "CASH_DRAWER_OPEN",
              orderGuid: null,
              taskId: input.eventId,
              printerId: input.printerId,
              action: "open-cash-drawer",
              reason: "MANUAL",
              permissionCode:
                "Permissions.PosTerminal.CashDrawer.Open",
            },
          );
        assertSameAuthorization(
          persistedAuthorization,
          authorization.context,
        );
        return {
          kind: "existing",
          event,
        };
      }

      const inserted = await tx.run(
        `INSERT INTO drawer_events (
          event_id, order_guid, printer_id, print_job_id, state, reason,
          retry_count, requested_at_iso, completed_at_iso, last_error_code,
          created_at_iso, updated_at_iso
        ) VALUES (?, NULL, ?, NULL, 'Requested', 'MANUAL', 0, ?, NULL, NULL, ?, ?)`,
        [input.eventId, input.printerId, now, now, now],
      );
      if (inserted.changes !== 1) {
        throw new Error("Manual drawer action could not be persisted.");
      }
      await insertFulfilmentAudit(tx, authorization.audit);
      return {
        kind: "created",
        event: {
          eventId: input.eventId,
          orderGuid: null,
          printerId: input.printerId,
          state: "Requested",
          reason: "MANUAL",
          retryCount: 0,
        },
      };
    });
  }

  public async listQueuedPrintJobs(): Promise<readonly StoredFulfilmentPrintJob[]> {
    const rows = await this.db.getAll<PrintRow>("SELECT * FROM print_jobs WHERE state = 'Queued' ORDER BY created_at_iso ASC, job_id ASC");
    // 自动 drain 仅用 jobId；避免枚举时解密多份小票，领取成功后才读取加密字节。
    return rows.map(mapPrintJobMetadata);
  }

  public async listRequiredDrawerEvents(): Promise<readonly StoredFulfilmentDrawerEvent[]> {
    const rows = await this.db.getAll<DrawerRow>("SELECT * FROM drawer_events WHERE state = 'Required' ORDER BY created_at_iso ASC, event_id ASC");
    return rows.map(mapDrawerEvent);
  }

  public claimQueuedPrintJob(jobId: string): Promise<StoredFulfilmentPrintJob | null> { return this.claimPrint(jobId, "Queued", false); }
  public beginManualPrintRetry(jobId: string): Promise<StoredFulfilmentPrintJob | null> { return this.claimPrint(jobId, "Failed", true); }

  public finishPrintJob(
    jobId: string,
    expected: "Sending",
    state: "Printed" | "Failed" | "Ambiguous",
    errorCode: string | null,
    audit: FulfilmentAuditEvent | null,
  ): Promise<boolean> {
    const now = this.options.nowIso();
    return this.db.withExclusiveTransaction(async (tx) => {
      const changed = await tx.run(
        "UPDATE print_jobs SET state = ?, last_error_code = ?, updated_at_iso = ? WHERE job_id = ? AND state = ?",
        [state, errorCode, now, jobId, expected],
      );
      if (changed.changes !== 1) return false;

      const job = await tx.getFirst<{
        order_guid: unknown;
        is_reprint: unknown;
        printer_id: unknown;
      }>(
        "SELECT order_guid, is_reprint, printer_id FROM print_jobs WHERE job_id = ?",
        [jobId],
      );
      if (!job) throw new Error("Finished print job could not be reloaded.");
      const orderGuid = text(job.order_guid);
      if (audit === null) {
        if (booleanInt(job.is_reprint)) {
          throw new Error("A reprint finish requires a RECEIPT_REPRINT audit.");
        }
        // DB 无法区分普通自动打印和普通小票的人工重试；非重打任务暂允许无审计。
        return true;
      }
      assertFulfilmentAudit(
        audit,
        "RECEIPT_REPRINT",
        orderGuid,
        jobId,
        state,
        text(job.printer_id),
      );
      await insertFulfilmentAudit(tx, audit);
      return true;
    });
  }

  public claimRequiredDrawerEvent(eventId: string): Promise<StoredFulfilmentDrawerEvent | null> { return this.claimDrawer(eventId, "Required", false); }
  public beginManualDrawerRetry(eventId: string): Promise<StoredFulfilmentDrawerEvent | null> { return this.claimDrawer(eventId, "Failed", true); }

  public finishDrawerEvent(
    eventId: string,
    expected: "Requested",
    state: "Completed" | "Failed" | "Unknown",
    errorCode: string | null,
    audit: FulfilmentAuditEvent,
  ): Promise<boolean> {
    const now = this.options.nowIso();
    const completedAt = state === "Completed" ? now : null;
    return this.db.withExclusiveTransaction(async (tx) => {
      const changed = await tx.run(
        "UPDATE drawer_events SET state = ?, last_error_code = ?, completed_at_iso = ?, updated_at_iso = ? WHERE event_id = ? AND state = ?",
        [state, errorCode, completedAt, now, eventId, expected],
      );
      if (changed.changes !== 1) return false;

      const drawer = await tx.getFirst<{
        order_guid: unknown;
        printer_id: unknown;
      }>(
        "SELECT order_guid, printer_id FROM drawer_events WHERE event_id = ?",
        [eventId],
      );
      if (!drawer) throw new Error("Finished drawer event could not be reloaded.");
      assertFulfilmentAudit(
        audit,
        "CASH_DRAWER_OPEN",
        nullableText(drawer.order_guid),
        eventId,
        state,
        text(drawer.printer_id),
      );
      await insertFulfilmentAudit(tx, audit);
      return true;
    });
  }

  /**
   * DB 层没有权利用原始字节伪造“重打”标记；调用方必须先让 receipt domain
   * 生成携带重打文本的 ESC/POS bytes，再传入本方法。
   */
  public async createLastReceiptReprint(input: Readonly<{
    orderGuid: string;
    receiptBytes: Uint8Array;
    printerId: string;
  }>, authorization?: FulfilmentInitialAuthorization): Promise<StoredFulfilmentPrintJob | null> {
    assertPrinterId(input.printerId);
    if (authorization) {
      assertInitialAuthorization(
        authorization,
        {
          eventType: "RECEIPT_REPRINT",
          orderGuid: input.orderGuid,
          taskId: authorization.context.actionId,
          printerId: input.printerId,
          action: "reprint-last-receipt",
          reason: "last-receipt",
          permissionCode:
            "Permissions.PosTerminal.Receipt.PrintLast",
        },
      );
    }
    const encryptedReceipt = await this.options.encryptor.encrypt(
      encodeReceipt(input.receiptBytes),
    );
    return this.db.withExclusiveTransaction(async (tx) => {
      const order = await tx.getFirst<{ state: unknown }>(
        "SELECT state FROM local_orders WHERE order_guid = ?",
        [input.orderGuid],
      );
      if (!order || !isReprintableOrderState(order.state)) return null;

      // 中文注释：orderGuid 只能来自调用方已经选择并重新渲染的订单，禁止从历史打印作业反推。
      const job: PersistedPrintJobInput = {
        jobId:
          authorization?.context.actionId ??
          this.options.createPrintJobId(),
        orderGuid: input.orderGuid,
        printerId: input.printerId,
        receiptBytes: input.receiptBytes,
        isReprint: true,
      };
      if (authorization) {
        const existing = await tx.getFirst<PrintRow>(
          "SELECT * FROM print_jobs WHERE job_id = ?",
          [job.jobId],
        );
        if (existing) {
          const persisted =
            await assertMatchingAuthorizedReprint(
              existing,
              job,
              this.options.encryptor,
            );
          const persistedAuthorization =
            await loadInitialAuthorization(
              tx,
              {
                eventType: "RECEIPT_REPRINT",
                orderGuid: job.orderGuid,
                taskId: job.jobId,
                printerId: job.printerId,
                action: "reprint-last-receipt",
                reason: "last-receipt",
                permissionCode:
                  "Permissions.PosTerminal.Receipt.PrintLast",
              },
            );
          assertSameAuthorization(
            persistedAuthorization,
            authorization.context,
          );
          return {
            ...persisted,
            authorization: persistedAuthorization,
          };
        }
      }
      await insertPrintJob(tx, job, encryptedReceipt, this.options.nowIso());
      if (authorization) {
        await insertFulfilmentAudit(tx, authorization.audit);
      }
      return {
        jobId: job.jobId,
        orderGuid: job.orderGuid,
        printerId: job.printerId,
        isReprint: true,
        bytes: job.receiptBytes,
        state: "Queued",
        retryCount: 0,
        ...(authorization
          ? { authorization: authorization.context }
          : {}),
      };
    });
  }

  private async claimPrint(jobId: string, expected: "Queued" | "Failed", manual: boolean): Promise<StoredFulfilmentPrintJob | null> {
    return this.db.withExclusiveTransaction(async (tx) => {
      const row = await tx.getFirst<PrintRow>("SELECT * FROM print_jobs WHERE job_id = ?", [jobId]);
      if (!row || printState(row.state) !== expected) return null;
      const retryCount = int(row.retry_count);
      let bytes: Uint8Array;
      try {
        bytes = decodeReceipt(await this.options.encryptor.decrypt(ciphertext(row.receipt_ciphertext)));
      } catch {
        // 解密失败时没有安全的硬件字节，转为 Failed 后只允许人工检查，不让自动队列无限重试。
        if (expected === "Queued") await tx.run("UPDATE print_jobs SET state = 'Failed', last_error_code = 'RECEIPT_DECRYPT_FAILED', updated_at_iso = ? WHERE job_id = ? AND state = 'Queued'", [this.options.nowIso(), jobId]);
        return null;
      }
      const now = this.options.nowIso();
      const result = manual
        ? await tx.run("UPDATE print_jobs SET state = 'Sending', retry_count = retry_count + 1, updated_at_iso = ? WHERE job_id = ? AND state = 'Failed'", [now, jobId])
        : await tx.run("UPDATE print_jobs SET state = 'Sending', updated_at_iso = ? WHERE job_id = ? AND state = ?", [now, jobId, expected]);
      if (result.changes !== 1) return null;
      const authorization = booleanInt(row.is_reprint)
        ? await loadInitialAuthorization(
            tx,
            {
              eventType: "RECEIPT_REPRINT",
              orderGuid: text(row.order_guid),
              taskId: text(row.job_id),
              printerId: text(row.printer_id),
              action: "reprint-last-receipt",
              reason: "last-receipt",
              permissionCode:
                "Permissions.PosTerminal.Receipt.PrintLast",
            },
            true,
          )
        : null;
      return {
        ...mapPrintJobMetadata(row),
        bytes,
        state: "Sending",
        retryCount: retryCount + (manual ? 1 : 0),
        ...(authorization ? { authorization } : {}),
      };
    });
  }

  private async claimDrawer(eventId: string, expected: "Required" | "Failed", manual: boolean): Promise<StoredFulfilmentDrawerEvent | null> {
    return this.db.withExclusiveTransaction(async (tx) => {
      const row = await tx.getFirst<DrawerRow>("SELECT * FROM drawer_events WHERE event_id = ?", [eventId]);
      if (!row || drawerState(row.state) !== expected) return null;
      const retryCount = int(row.retry_count);
      const now = this.options.nowIso();
      const result = manual
        ? await tx.run("UPDATE drawer_events SET state = 'Requested', retry_count = retry_count + 1, requested_at_iso = ?, updated_at_iso = ? WHERE event_id = ? AND state = 'Failed'", [now, now, eventId])
        : await tx.run("UPDATE drawer_events SET state = 'Requested', requested_at_iso = ?, updated_at_iso = ? WHERE event_id = ? AND state = ?", [now, now, eventId, expected]);
      if (result.changes !== 1) return null;
      return { ...mapDrawerEvent(row), state: "Requested", retryCount: retryCount + (manual ? 1 : 0) };
    });
  }
}

async function insertPrintJob(db: SqliteConnectionPort, input: PersistedPrintJobInput, encryptedReceipt: Uint8Array, nowIso: string): Promise<void> {
  assertPrinterId(input.printerId);
  await db.run(`INSERT INTO print_jobs (job_id, order_guid, state, printer_id, receipt_ciphertext, is_reprint, retry_count, last_error_code, created_at_iso, updated_at_iso) VALUES (?, ?, 'Queued', ?, ?, ?, 0, NULL, ?, ?)`, [input.jobId, input.orderGuid, input.printerId, encryptedReceipt, input.isReprint ? 1 : 0, nowIso, nowIso]);
}

async function insertDrawerEvent(db: SqliteConnectionPort, input: PersistedDrawerEventInput, nowIso: string): Promise<void> {
  assertPrinterId(input.printerId);
  if (input.printJobId !== null) {
    const linkedPrint = await db.getFirst<{ printer_id: unknown }>(
      "SELECT printer_id FROM print_jobs WHERE job_id = ?",
      [input.printJobId],
    );
    if (!linkedPrint || text(linkedPrint.printer_id) !== input.printerId) {
      throw new Error("Cash drawer printer does not match its linked print job.");
    }
  }
  await db.run(`INSERT INTO drawer_events (event_id, order_guid, printer_id, print_job_id, state, reason, retry_count, requested_at_iso, completed_at_iso, last_error_code, created_at_iso, updated_at_iso) VALUES (?, ?, ?, ?, 'Required', ?, 0, NULL, NULL, NULL, ?, ?)`, [input.eventId, input.orderGuid, input.printerId, input.printJobId, input.reason, nowIso, nowIso]);
}

async function insertFulfilmentAudit(
  db: SqliteConnectionPort,
  audit: FulfilmentAuditEvent,
): Promise<void> {
  await db.run(
    "INSERT INTO audit_events (event_id, event_type, occurred_at_iso, order_guid, correlation_id, payload_json, uploaded_at_iso) VALUES (?, ?, ?, ?, ?, ?, NULL)",
    [
      audit.eventId,
      audit.eventType,
      audit.occurredAtIso,
      audit.orderGuid,
      audit.correlationId,
      JSON.stringify(audit.payload),
    ],
  );
}

const fulfilmentAuditPayloadKeys = new Set([
  "action",
  "status",
  "reason",
  "source",
  "outcome",
  "printerId",
  "errorCode",
  "requestingCashierId",
  "authorizingCashierId",
  "permissionCode",
  "authorizationMode",
]);

type FulfilmentAuditEventType = "RECEIPT_REPRINT" | "CASH_DRAWER_OPEN";
type FulfilmentTerminalState =
  | "Printed"
  | "Failed"
  | "Ambiguous"
  | "Completed"
  | "Unknown";

type InitialAuthorizationExpectation = Readonly<{
  eventType: FulfilmentAuditEventType;
  orderGuid: string | null;
  taskId: string;
  printerId: string;
  action: "reprint-last-receipt" | "open-cash-drawer";
  reason: "last-receipt" | "MANUAL";
  permissionCode:
    | "Permissions.PosTerminal.Receipt.PrintLast"
    | "Permissions.PosTerminal.CashDrawer.Open";
}>;

function assertFulfilmentAudit(
  audit: FulfilmentAuditEvent,
  expectedType: FulfilmentAuditEventType,
  orderGuid: string | null,
  taskId: string,
  terminalState: FulfilmentTerminalState,
  printerId: string,
): void {
  assertSafeAuditEnvelope(audit, expectedType, orderGuid);
  const expectedOutcome =
    terminalState === "Printed" || terminalState === "Completed"
      ? "Succeeded"
      : "Failed";
  if (
    audit.correlationId !== taskId ||
    audit.payload.status !== terminalState ||
    audit.payload.outcome !== expectedOutcome ||
    audit.payload.printerId !== printerId
  ) {
    throw new Error("Fulfilment audit result does not match the persisted hardware task.");
  }
}

function assertSafeAuditEnvelope(
  audit: FulfilmentAuditEvent,
  expectedType: FulfilmentAuditEventType,
  orderGuid: string | null,
): void {
  if (audit.eventType !== expectedType) {
    throw new Error(`Fulfilment audit event type must be ${expectedType}.`);
  }
  if (audit.orderGuid !== orderGuid) {
    throw new Error("Fulfilment audit belongs to a different order.");
  }
  if (
    !audit.eventId.trim() ||
    !audit.correlationId.trim() ||
    !audit.occurredAtIso.trim() ||
    !Number.isFinite(Date.parse(audit.occurredAtIso))
  ) {
    throw new Error("Fulfilment audit identity and timestamp are invalid.");
  }
  if (!audit.payload || typeof audit.payload !== "object" || Array.isArray(audit.payload)) {
    throw new Error("Fulfilment audit payload must be a safe object.");
  }
  for (const [key, value] of Object.entries(audit.payload)) {
    if (!fulfilmentAuditPayloadKeys.has(key)) {
      throw new Error(`Fulfilment audit payload field is not allowed or sensitive: ${key}.`);
    }
    if (
      value !== null &&
      typeof value !== "string" &&
      (typeof value !== "number" || !Number.isFinite(value))
    ) {
      throw new Error(`Fulfilment audit payload value is invalid: ${key}.`);
    }
    if (typeof value === "string" && value.length > 512) {
      throw new Error(`Fulfilment audit payload value is too long: ${key}.`);
    }
  }
}

function assertInitialAuthorization(
  authorization: FulfilmentInitialAuthorization,
  expected: InitialAuthorizationExpectation,
): void {
  assertSameAuthorization(
    authorization.context,
    authorization.context,
  );
  assertSafeAuditEnvelope(
    authorization.audit,
    expected.eventType,
    expected.orderGuid,
  );
  const context = authorization.context;
  const payload = authorization.audit.payload;
  if (
    context.actionId !== expected.taskId ||
    authorization.audit.correlationId !== expected.taskId ||
    context.permissionCode !== expected.permissionCode ||
    payload.action !== expected.action ||
    payload.status !== "Authorized" ||
    payload.reason !== expected.reason ||
    payload.source !== "sales" ||
    payload.outcome !== "Succeeded" ||
    payload.printerId !== expected.printerId ||
    payload.errorCode !== null ||
    payload.requestingCashierId !== context.requestingCashierId ||
    payload.authorizingCashierId !== context.authorizingCashierId ||
    payload.permissionCode !== context.permissionCode ||
    payload.authorizationMode !== context.authorizationMode
  ) {
    throw new Error(
      "Fulfilment authorization audit does not match its durable task.",
    );
  }
}

async function loadInitialAuthorization(
  db: SqliteConnectionPort,
  expected: InitialAuthorizationExpectation,
  allowMissing = false,
): Promise<PersistedFulfilmentAuthorization | null> {
  const rows = await db.getAll<Record<string, unknown>>(
    `SELECT event_id, event_type, occurred_at_iso, order_guid,
      correlation_id, payload_json
     FROM audit_events
     WHERE correlation_id = ? AND event_type = ?
     ORDER BY rowid ASC`,
    [expected.taskId, expected.eventType],
  );
  for (const row of rows) {
    const payload = auditPayload(row.payload_json);
    if (payload.status !== "Authorized") continue;
    const audit: FulfilmentAuditEvent = {
      eventId: text(row.event_id),
      eventType: text(row.event_type),
      occurredAtIso: text(row.occurred_at_iso),
      orderGuid: nullableText(row.order_guid),
      correlationId: text(row.correlation_id),
      payload,
    };
    const context = authorizationContextFromAudit(
      expected.taskId,
      payload,
    );
    assertInitialAuthorization({ context, audit }, expected);
    return context;
  }
  if (allowMissing) return null;
  throw new Error("Fulfilment authorization audit is missing.");
}

function authorizationContextFromAudit(
  actionId: string,
  payload: Readonly<Record<string, string | number | null>>,
): PersistedFulfilmentAuthorization {
  const authorizationMode = payload.authorizationMode;
  if (
    authorizationMode !== "current-cashier" &&
    authorizationMode !== "offline-cache" &&
    authorizationMode !== "online"
  ) {
    throw new Error("Fulfilment authorization mode is invalid.");
  }
  return {
    actionId,
    permissionCode: auditText(
      payload.permissionCode,
      "Fulfilment permission",
    ),
    authorizationMode,
    requestingCashierId: auditText(
      payload.requestingCashierId,
      "Requesting cashier id",
    ),
    authorizingCashierId:
      payload.authorizingCashierId === null
        ? null
        : auditText(
            payload.authorizingCashierId,
            "Authorizing cashier id",
          ),
  };
}

function assertSameAuthorization(
  actual: PersistedFulfilmentAuthorization | null,
  expected: PersistedFulfilmentAuthorization,
): asserts actual is PersistedFulfilmentAuthorization {
  if (
    !actual ||
    !expected.actionId.trim() ||
    !expected.permissionCode.trim() ||
    !expected.requestingCashierId.trim() ||
    actual.actionId !== expected.actionId ||
    actual.permissionCode !== expected.permissionCode ||
    actual.authorizationMode !== expected.authorizationMode ||
    actual.requestingCashierId !== expected.requestingCashierId ||
    actual.authorizingCashierId !== expected.authorizingCashierId ||
    (expected.authorizationMode === "current-cashier"
      ? expected.authorizingCashierId !== null
      : expected.authorizingCashierId === null)
  ) {
    throw new Error("Fulfilment authorization identity is inconsistent.");
  }
}

async function assertMatchingAuthorizedReprint(
  row: PrintRow,
  expected: PersistedPrintJobInput,
  encryptor: SensitivePayloadEncryptor,
): Promise<StoredFulfilmentPrintJob> {
  const persisted = mapPrintJobMetadata(row);
  if (
    persisted.jobId !== expected.jobId ||
    persisted.orderGuid !== expected.orderGuid ||
    persisted.printerId !== expected.printerId ||
    !persisted.isReprint
  ) {
    throw new Error("Authorized receipt reprint action conflict.");
  }
  const bytes = decodeReceipt(
    await encryptor.decrypt(
      ciphertext(row.receipt_ciphertext),
    ),
  );
  if (!sameBytes(bytes, expected.receiptBytes)) {
    throw new Error("Authorized receipt reprint action conflict.");
  }
  return { ...persisted, bytes };
}

function auditPayload(
  value: unknown,
): Readonly<Record<string, string | number | null>> {
  if (typeof value !== "string") {
    throw new Error("Fulfilment audit payload is invalid.");
  }
  const parsed: unknown = JSON.parse(value);
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    throw new Error("Fulfilment audit payload is invalid.");
  }
  return parsed as Readonly<Record<string, string | number | null>>;
}

function auditText(value: unknown, name: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`${name} is required.`);
  }
  return value.trim();
}

function sameBytes(left: Uint8Array, right: Uint8Array): boolean {
  if (left.length !== right.length) return false;
  return left.every((value, index) => value === right[index]);
}

type PrintRow = Record<string, unknown>;
type DrawerRow = Record<string, unknown>;

function mapPrintJobMetadata(row: PrintRow): StoredFulfilmentPrintJob { return { jobId: text(row.job_id), orderGuid: text(row.order_guid), printerId: text(row.printer_id), isReprint: booleanInt(row.is_reprint), bytes: new Uint8Array(), state: printState(row.state), retryCount: int(row.retry_count) }; }
function mapDrawerEvent(row: DrawerRow): StoredFulfilmentDrawerEvent { return { eventId: text(row.event_id), orderGuid: nullableText(row.order_guid), printerId: text(row.printer_id), state: drawerState(row.state), reason: text(row.reason), retryCount: int(row.retry_count) }; }
function assertMatchingManualDrawerEvent(
  row: DrawerRow,
  input: ManualDrawerOpenInput,
): StoredFulfilmentDrawerEvent {
  const event = mapDrawerEvent(row);
  if (
    event.orderGuid !== null ||
    row.print_job_id !== null ||
    event.printerId !== input.printerId ||
    event.reason !== "MANUAL" ||
    event.state === "Required"
  ) {
    throw new Error("Manual drawer action conflict.");
  }
  return event;
}
function assertFulfilmentId(value: string, name: string): void { if (!value.trim()) throw new Error(`${name} is required.`); }
function assertPrinterId(value: string): void { if (!value.trim()) throw new Error("Fulfilment printer id is required."); }
function encodeReceipt(bytes: Uint8Array): string { return JSON.stringify(Array.from(bytes)); }
function decodeReceipt(serialized: string): Uint8Array { const parsed: unknown = JSON.parse(serialized); if (!Array.isArray(parsed) || parsed.some((value) => !Number.isInteger(value) || value < 0 || value > 255)) throw new Error("Invalid encrypted receipt payload."); return Uint8Array.from(parsed as number[]); }
function ciphertext(value: unknown): Uint8Array { if (!(value instanceof Uint8Array)) throw new Error("Invalid receipt ciphertext."); return value; }
function text(value: unknown): string { if (typeof value !== "string" || !value) throw new Error("Invalid fulfilment text."); return value; }
function nullableText(value: unknown): string | null { return value === null || value === undefined ? null : text(value); }
function int(value: unknown): number { const number = Number(value); if (!Number.isSafeInteger(number) || number < 0) throw new Error("Invalid fulfilment integer."); return number; }
function booleanInt(value: unknown): boolean { if (value === 0 || value === false) return false; if (value === 1 || value === true) return true; throw new Error("Invalid fulfilment boolean."); }
function printState(value: unknown): PrintJobState { const state = text(value); if (state === "Queued" || state === "Sending" || state === "Printed" || state === "Failed" || state === "Ambiguous") return state; throw new Error("Invalid print state."); }
function isReprintableOrderState(value: unknown): boolean {
  const state = text(value);
  if (state === "Draft" || state === "Completing") return false;
  if (
    state === "CompletedLocal" ||
    state === "PendingSync" ||
    state === "Syncing" ||
    state === "Synced" ||
    state === "Blocked403" ||
    state === "Rejected"
  ) {
    return true;
  }
  throw new Error("Invalid order state for receipt reprint.");
}
function drawerState(value: unknown): DrawerEventState { const state = text(value); if (state === "Required" || state === "Requested" || state === "Completed" || state === "Failed" || state === "Unknown") return state; throw new Error("Invalid drawer state."); }
