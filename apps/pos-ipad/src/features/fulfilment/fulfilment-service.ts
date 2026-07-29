import type { CashDrawerPort, DrawerResult, PrintResult, PrinterPort } from "@/core/contracts";

export type FulfilmentPrintJob = Readonly<{
  jobId: string;
  orderGuid: string;
  printerId: string;
  /** reprint 作业的 ESC/POS bytes 必须已包含重打标记，不能直接复用原始字节。 */
  isReprint: boolean;
  bytes: Uint8Array;
  state: "Queued" | "Sending" | "Printed" | "Failed" | "Ambiguous";
  retryCount: number;
  /** 最后小票重打从首份耐久授权审计恢复；普通打印与历史页重打没有该字段。 */
  authorization?: FulfilmentAuthorizationContext;
}>;

export type FulfilmentDrawerEvent = Readonly<{
  eventId: string;
  orderGuid: string | null;
  /** 钱箱脉冲必须通过订单提交时冻结的芯烨打印机发送。 */
  printerId: string;
  state: "Required" | "Requested" | "Completed" | "Failed" | "Unknown";
  reason: string;
  retryCount: number;
}>;

export type FulfilmentAuthorizationContext = Readonly<{
  /** 由授权服务创建的一次动作稳定标识，不得使用条码或授权票据。 */
  actionId: string;
  permissionCode: string;
  authorizationMode: "current-cashier" | "offline-cache" | "online";
  requestingCashierId: string;
  authorizingCashierId: string | null;
}>;

export type FulfilmentLeaseGuard = () => void;

export type PreparedManualDrawerOpen = Readonly<{
  /** 组合根从持久设置读取并冻结，UI 不得选择或覆盖。 */
  printerId: string;
}>;

export type ManualDrawerOpenBeginResult =
  | Readonly<{ kind: "created"; event: FulfilmentDrawerEvent }>
  | Readonly<{ kind: "existing"; event: FulfilmentDrawerEvent }>;

export type FulfilmentInitialAuthorization = Readonly<{
  context: FulfilmentAuthorizationContext;
  audit: FulfilmentAuditEvent;
}>;

/** receipt domain 已生成的重打小票；bytes 必须含有 WPF 对齐的“重打”标记。 */
export type PreparedLastReceiptReprint = Readonly<{
  /** 由订单账本/调用方选定的不可变订单；store 不得从旧 print_jobs 猜测或替换。 */
  orderGuid: string;
  receiptBytes: Uint8Array;
  printerId: string;
}>;

/**
 * 这是 fulfilment 功能的私有持久化边界。
 *
 * 任务必须随已完成订单在同一 SQLCipher 事务中落库；本服务只消费已持久化工作，
 * 因而打印或开钱箱失败绝不会影响订单、tender 或 outbox。
 */
export interface FulfilmentStore {
  listQueuedPrintJobs(): Promise<readonly FulfilmentPrintJob[]>;
  listRequiredDrawerEvents(): Promise<readonly FulfilmentDrawerEvent[]>;
  claimQueuedPrintJob(jobId: string): Promise<FulfilmentPrintJob | null>;
  beginManualPrintRetry(jobId: string): Promise<FulfilmentPrintJob | null>;
  finishPrintJob(
    jobId: string,
    expected: "Sending",
    state: "Printed" | "Failed" | "Ambiguous",
    errorCode: string | null,
    audit: FulfilmentAuditEvent | null,
  ): Promise<boolean>;
  claimRequiredDrawerEvent(eventId: string): Promise<FulfilmentDrawerEvent | null>;
  beginManualDrawerRetry(eventId: string): Promise<FulfilmentDrawerEvent | null>;
  beginManualDrawerOpen(input: Readonly<{
    eventId: string;
    printerId: string;
    reason: "MANUAL";
  }>, authorization: FulfilmentInitialAuthorization): Promise<ManualDrawerOpenBeginResult>;
  finishDrawerEvent(
    eventId: string,
    expected: "Requested",
    state: "Completed" | "Failed" | "Unknown",
    errorCode: string | null,
    audit: FulfilmentAuditEvent,
  ): Promise<boolean>;
  /**
   * 原子地写入一份新作业；必须使用 input.orderGuid，不得查询历史 Printed job
   * 推断订单；调用方同时提供 receipt domain 预渲染的真实重打字节。
   */
  createLastReceiptReprint(
    input: PreparedLastReceiptReprint,
    authorization?: FulfilmentInitialAuthorization,
  ): Promise<FulfilmentPrintJob | null>;
}

export type FulfilmentAuditEvent = Readonly<{
  eventId: string;
  eventType: string;
  occurredAtIso: string;
  orderGuid: string | null;
  correlationId: string;
  payload: Readonly<Record<string, string | number | null>>;
}>;

export type FulfilmentServiceOptions = Readonly<{
  store: FulfilmentStore;
  printer: Pick<PrinterPort, "connect" | "print">;
  drawer: Pick<CashDrawerPort, "open">;
  nowIso(): string;
  createAuditId(): string;
  createCorrelationId(): string;
  /** 由订单账本选择 orderGuid 并重新渲染；DB 层不得改绑订单或复制旧 bytes 伪造重打。 */
  prepareLastReceiptReprint(): Promise<PreparedLastReceiptReprint | null>;
  /** 历史页只交付订单号；实现仍须从本地账本和冻结设置重新渲染。 */
  prepareReceiptReprint?(
    orderGuid: string,
  ): Promise<PreparedLastReceiptReprint | null>;
  /** 每次手动开箱只读一次当前持久设置，并冻结该动作使用的打印机。 */
  prepareManualDrawerOpen?(): Promise<PreparedManualDrawerOpen | null>;
}>;

export type FulfilmentActionResult = Readonly<{
  state: "Printed" | "Failed" | "Ambiguous" | "Completed" | "Unknown" | "recovery-required" | "not-retryable" | "not-found";
  errorCode: string | null;
}>;

/**
 * 打印和钱箱是订单提交后的附属动作。状态声明式地限制自动队列仅消费 Queued/Required，
 * 避免断电、蓝牙超时或用户重复点击造成盲目重打、重复开箱。
 */
export class FulfilmentService {
  private hardwareTail: Promise<void> = Promise.resolve();

  public constructor(private readonly options: FulfilmentServiceOptions) {}

  public async drainAutomaticQueue(): Promise<Readonly<{ printed: number; drawersOpened: number }>> {
    return this.serializeHardware(() => this.drainAutomaticQueueUnsafe());
  }

  public async retryFailedPrint(jobId: string): Promise<FulfilmentActionResult> {
    return this.serializeHardware(() => this.retryFailedPrintUnsafe(jobId));
  }

  public async retryFailedDrawer(eventId: string): Promise<FulfilmentActionResult> {
    return this.serializeHardware(() => this.retryFailedDrawerUnsafe(eventId));
  }

  public async reprintLastReceipt(
    authorization: FulfilmentAuthorizationContext,
    assertActive: FulfilmentLeaseGuard,
  ): Promise<FulfilmentActionResult> {
    const trustedAuthorization = normalizeAuthorization(
      authorization,
      "Permissions.PosTerminal.Receipt.PrintLast",
    );
    return this.serializeHardware(
      () =>
        this.reprintLastReceiptUnsafe(
          trustedAuthorization,
          assertActive,
        ),
      assertActive,
    );
  }

  public async reprintReceipt(
    orderGuid: string,
  ): Promise<FulfilmentActionResult> {
    const normalized = orderGuid.trim();
    if (!normalized) return { state: "not-found", errorCode: null };
    return this.serializeHardware(() =>
      this.reprintReceiptUnsafe(normalized),
    );
  }

  public async openDrawerManually(
    authorization: FulfilmentAuthorizationContext,
    assertActive: FulfilmentLeaseGuard,
  ): Promise<FulfilmentActionResult> {
    const trustedAuthorization = normalizeAuthorization(
      authorization,
      "Permissions.PosTerminal.CashDrawer.Open",
    );
    return this.serializeHardware(
      () =>
        this.openDrawerManuallyUnsafe(
          trustedAuthorization,
          assertActive,
        ),
      assertActive,
    );
  }

  private async drainAutomaticQueueUnsafe(): Promise<Readonly<{ printed: number; drawersOpened: number }>> {
    let printed = 0;
    let drawersOpened = 0;

    // 中文注释：只能自动声明 Queued；Failed 需要人为确认，Sending/Ambiguous 绝不可猜测重放。
    for (const candidate of await this.options.store.listQueuedPrintJobs()) {
      const result = await this.sendQueuedPrint(candidate.jobId, false);
      if (result.state === "Printed") printed += 1;
    }

    // 中文注释：钱箱与打印相互独立，现金订单已提交后即可请求开箱；状态不确定绝不自动补脉冲。
    for (const candidate of await this.options.store.listRequiredDrawerEvents()) {
      const result = await this.sendRequiredDrawer(candidate.eventId, false);
      if (result.state === "Completed") drawersOpened += 1;
    }

    return { printed, drawersOpened };
  }

  private async retryFailedPrintUnsafe(jobId: string): Promise<FulfilmentActionResult> {
    const job = await this.options.store.beginManualPrintRetry(jobId);
    if (!job) return { state: "not-retryable", errorCode: null };

    return this.sendClaimedPrint(job, true);
  }

  private async retryFailedDrawerUnsafe(eventId: string): Promise<FulfilmentActionResult> {
    const event = await this.options.store.beginManualDrawerRetry(eventId);
    if (!event) return { state: "not-retryable", errorCode: null };

    return this.sendClaimedDrawer(event, true);
  }

  private async reprintLastReceiptUnsafe(
    authorization: FulfilmentAuthorizationContext,
    assertActive: FulfilmentLeaseGuard,
  ): Promise<FulfilmentActionResult> {
    // 中文注释：先由 receipt domain 生成带“重打”标记的真实 ESC/POS 字节，不能让存储层复制原小票。
    const prepared = await this.options.prepareLastReceiptReprint();
    if (!prepared) return { state: "not-found", errorCode: null };
    // 准备器可能跨越 SQLite/设置异步边界；首个耐久写入前必须再次确认原收银员 lease。
    assertActive();

    const initialAuthorization = {
      context: authorization,
      audit: this.createAuditEvent(
        "RECEIPT_REPRINT",
        prepared.orderGuid,
        authorization.actionId,
        {
          action: "reprint-last-receipt",
          status: "Authorized",
          reason: "last-receipt",
          source: "sales",
          outcome: "Succeeded",
          printerId: safeAuditText(prepared.printerId),
          errorCode: null,
          ...authorizationAuditPayload(authorization),
        },
      ),
    } satisfies FulfilmentInitialAuthorization;
    return this.createAndSendReprint(
      prepared,
      "last-receipt",
      initialAuthorization,
    );
  }

  private async reprintReceiptUnsafe(
    orderGuid: string,
  ): Promise<FulfilmentActionResult> {
    const prepare = this.options.prepareReceiptReprint;
    if (!prepare) return { state: "not-found", errorCode: null };
    const prepared = await prepare(orderGuid);
    // 账本准备器不得把历史页选择静默改绑到另一订单。
    if (!prepared || prepared.orderGuid !== orderGuid) {
      return { state: "not-found", errorCode: null };
    }
    return this.createAndSendReprint(prepared, "remote-history");
  }

  private async createAndSendReprint(
    prepared: PreparedLastReceiptReprint,
    source: "last-receipt" | "remote-history",
    authorization?: FulfilmentInitialAuthorization,
  ): Promise<FulfilmentActionResult> {
    const job = await this.options.store.createLastReceiptReprint(
      prepared,
      authorization,
    );
    if (!job) return { state: "not-found", errorCode: null };

    if (job.state === "Queued") {
      return this.sendQueuedPrint(
        job.jobId,
        true,
        source,
      );
    }
    if (job.state === "Sending") return recoveryRequired();
    return { state: job.state, errorCode: null };
  }

  private async sendQueuedPrint(
    jobId: string,
    manual: boolean,
    reprintSource?: "last-receipt" | "remote-history",
  ): Promise<FulfilmentActionResult> {
    const job = await this.options.store.claimQueuedPrintJob(jobId);
    if (!job) return { state: "not-retryable", errorCode: null };
    return this.sendClaimedPrint(
      job,
      manual,
      reprintSource,
    );
  }

  private async sendClaimedPrint(
    job: FulfilmentPrintJob,
    manual: boolean,
    reprintSource?: "last-receipt" | "remote-history",
  ): Promise<FulfilmentActionResult> {
    let result: PrintResult;
    const connectionErrorCode = await this.connectPersistedPrinter(job.printerId);
    if (connectionErrorCode) {
      // 中文注释：连接阶段尚未发送任何小票字节，可以安全标记 Failed，等待人工重试原外设。
      result = { status: "failed", errorCode: connectionErrorCode };
    } else {
      try {
        result = await this.options.printer.print(job.jobId, job.bytes);
      } catch (error) {
        // 中文注释：驱动抛错时无法证明字节是否已送达，保守标为 Ambiguous，禁止自动重打。
        result = { status: "ambiguous", errorCode: errorCodeOf(error, "PRINT_EXCEPTION") };
      }
    }

    const state = printState(result);
    const shouldAuditAsReprint = manual || job.isReprint;
    const source = job.isReprint
      ? (reprintSource ?? "last-receipt")
      : "manual-retry";
    let audit: FulfilmentAuditEvent | null = null;
    if (shouldAuditAsReprint) {
      try {
        // 中文注释：审计必须在原子 finish 前构造，并描述本次真实硬件终态；CAS 失败时不得补写另一份。
        audit = this.createAuditEvent(
          "RECEIPT_REPRINT",
          job.orderGuid,
          job.jobId,
          {
            action: job.isReprint
              ? source === "remote-history"
                ? "reprint-history-receipt"
                : "reprint-last-receipt"
              : "retry-failed-print",
            status: state,
            reason: safeAuditText(result.errorCode) ?? (job.isReprint ? "last-receipt" : "manual-retry"),
            source,
            outcome: state === "Printed" ? "Succeeded" : "Failed",
            printerId: safeAuditText(job.printerId),
            errorCode: safeAuditText(result.errorCode),
            ...authorizationAuditPayload(job.authorization),
          },
        );
      } catch {
        return recoveryRequired();
      }
    }
    const completed = await finishOrFalse(() =>
      this.options.store.finishPrintJob(job.jobId, "Sending", state, result.errorCode, audit),
    );
    if (!completed) {
      // 中文注释：硬件终态与审计没有同时耐久化；禁止声称成功，也禁止 finish 后补写审计。
      return recoveryRequired();
    }
    return { state, errorCode: result.errorCode };
  }

  private async sendRequiredDrawer(eventId: string, manual: boolean): Promise<FulfilmentActionResult> {
    const event = await this.options.store.claimRequiredDrawerEvent(eventId);
    if (!event) return { state: "not-retryable", errorCode: null };
    return this.sendClaimedDrawer(event, manual);
  }

  private async openDrawerManuallyUnsafe(
    authorization: FulfilmentAuthorizationContext,
    assertActive: FulfilmentLeaseGuard,
  ): Promise<FulfilmentActionResult> {
    const prepare = this.options.prepareManualDrawerOpen;
    if (!prepare) return { state: "not-found", errorCode: null };
    const prepared = await prepare();
    if (!prepared) return { state: "not-found", errorCode: null };
    // 持久设置读取结束后、Requested 事件落库前复核，避免失效页面留下可执行钱箱任务。
    assertActive();

    const begun = await this.options.store.beginManualDrawerOpen(
      {
        eventId: authorization.actionId,
        printerId: prepared.printerId,
        reason: "MANUAL",
      },
      {
        context: authorization,
        audit: this.createAuditEvent(
          "CASH_DRAWER_OPEN",
          null,
          authorization.actionId,
          {
            action: "open-cash-drawer",
            status: "Authorized",
            reason: "MANUAL",
            source: "sales",
            outcome: "Succeeded",
            printerId: safeAuditText(prepared.printerId),
            errorCode: null,
            ...authorizationAuditPayload(authorization),
          },
        ),
      },
    );
    if (begun.kind === "existing") {
      if (begun.event.state === "Requested") return recoveryRequired();
      if (
        begun.event.state === "Completed" ||
        begun.event.state === "Failed" ||
        begun.event.state === "Unknown"
      ) {
        return { state: begun.event.state, errorCode: null };
      }
      throw new Error("Manual drawer action conflict.");
    }
    return this.sendClaimedDrawer(
      begun.event,
      false,
      authorization,
    );
  }

  private async sendClaimedDrawer(
    event: FulfilmentDrawerEvent,
    manual: boolean,
    authorization?: FulfilmentAuthorizationContext,
  ): Promise<FulfilmentActionResult> {
    let result: DrawerResult;
    const connectionErrorCode = await this.connectPersistedPrinter(event.printerId);
    if (connectionErrorCode) {
      // 中文注释：连接失败时没有发出 RJ11 脉冲，安全落为 Failed；人工重试仍只能连接原 printerId。
      result = { status: "failed", errorCode: connectionErrorCode };
    } else {
      try {
        result = await this.options.drawer.open(event.eventId);
      } catch (error) {
        // 中文注释：脉冲是否已经发出未知时，只能要求主管确认，绝不能再次自动开箱。
        result = { status: "unknown", errorCode: errorCodeOf(error, "DRAWER_EXCEPTION") };
      }
    }

    const state = drawerState(result);
    let audit: FulfilmentAuditEvent;
    try {
      // 所有钱箱动作都有 WPF 白名单审计，且必须和 Requested -> 终态 CAS 在同一存储事务提交。
      audit = this.createAuditEvent(
        "CASH_DRAWER_OPEN",
        event.orderGuid,
        event.eventId,
        {
          action: authorization ? "open-cash-drawer" : "open",
          status: state,
          reason: safeAuditText(event.reason),
          source: authorization
            ? "sales"
            : manual
              ? "manual-retry"
              : "automatic",
          outcome: state === "Completed" ? "Succeeded" : "Failed",
          printerId: safeAuditText(event.printerId),
          errorCode: safeAuditText(result.errorCode),
          ...authorizationAuditPayload(authorization),
        },
      );
    } catch {
      return recoveryRequired();
    }
    const completed = await finishOrFalse(() =>
      this.options.store.finishDrawerEvent(event.eventId, "Requested", state, result.errorCode, audit),
    );
    if (!completed) {
      // 中文注释：钱箱脉冲终态与审计没有同时耐久化；只能进入主管恢复，绝不再发脉冲或补审计。
      return recoveryRequired();
    }
    return { state, errorCode: result.errorCode };
  }

  private async connectPersistedPrinter(printerId: string): Promise<string | null> {
    if (!printerId.trim()) return "PRINTER_ID_MISSING";
    try {
      // 中文注释：连接目标只来自持久任务，运行时设置变化不得把旧订单改路由到新钱箱/打印机。
      await this.options.printer.connect(printerId);
      return null;
    } catch (error) {
      // 仅保留错误类型，不把原生异常 message（可能含系统路径或外设详情）写入审计。
      return errorCodeOf(error, "PRINTER_CONNECT_FAILED");
    }
  }

  private createAuditEvent(
    eventType: string,
    orderGuid: string | null,
    correlationId: string,
    payload: Readonly<Record<string, string | number | null>>,
  ): FulfilmentAuditEvent {
    return {
      eventId: this.options.createAuditId(),
      eventType,
      occurredAtIso: this.options.nowIso(),
      orderGuid,
      correlationId,
      payload,
    };
  }

  private serializeHardware<T>(
    operation: () => Promise<T>,
    assertActive?: FulfilmentLeaseGuard,
  ): Promise<T> {
    const guardedOperation = () => {
      // 授权动作必须在真正取得 BLE 队列所有权时复核，而不是只在排队前检查。
      assertActive?.();
      return operation();
    };
    const next = this.hardwareTail.then(
      guardedOperation,
      guardedOperation,
    );
    // 中文注释：无论一次作业成败，后续手动动作仍可继续，不让 rejected Promise 卡死 BLE 队列。
    this.hardwareTail = next.then(
      () => undefined,
      () => undefined,
    );
    return next;
  }
}

function printState(result: PrintResult): "Printed" | "Failed" | "Ambiguous" {
  return result.status === "printed" ? "Printed" : result.status === "failed" ? "Failed" : "Ambiguous";
}

function drawerState(result: DrawerResult): "Completed" | "Failed" | "Unknown" {
  return result.status === "completed" ? "Completed" : result.status === "failed" ? "Failed" : "Unknown";
}

function errorCodeOf(error: unknown, fallback: string): string {
  return error instanceof Error && error.name ? error.name : fallback;
}

function safeAuditText(value: string | null): string | null {
  if (value === null) return null;
  const normalized = value.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 128);
  return normalized || null;
}

function normalizeAuthorization(
  input: FulfilmentAuthorizationContext,
  expectedPermissionCode: string,
): FulfilmentAuthorizationContext {
  const actionId = requiredAuditText(input.actionId, "Fulfilment action id");
  const permissionCode = requiredAuditText(
    input.permissionCode,
    "Fulfilment permission",
  );
  if (permissionCode !== expectedPermissionCode) {
    throw new Error("Fulfilment authorization permission mismatch.");
  }
  const requestingCashierId = requiredAuditText(
    input.requestingCashierId,
    "Requesting cashier id",
  );
  const authorizingCashierId = input.authorizingCashierId === null
    ? null
    : requiredAuditText(
        input.authorizingCashierId,
        "Authorizing cashier id",
      );
  if (
    (input.authorizationMode === "current-cashier" &&
      authorizingCashierId !== null) ||
    (input.authorizationMode !== "current-cashier" &&
      authorizingCashierId === null)
  ) {
    throw new Error("Fulfilment authorization identity is inconsistent.");
  }
  return {
    actionId,
    permissionCode,
    authorizationMode: input.authorizationMode,
    requestingCashierId,
    authorizingCashierId,
  };
}

function authorizationAuditPayload(
  authorization?: FulfilmentAuthorizationContext,
): Readonly<Record<string, string | null>> {
  if (!authorization) return {};
  return {
    requestingCashierId: authorization.requestingCashierId,
    authorizingCashierId: authorization.authorizingCashierId,
    permissionCode: authorization.permissionCode,
    authorizationMode: authorization.authorizationMode,
  };
}

function requiredAuditText(value: string, name: string): string {
  const normalized = safeAuditText(value);
  if (!normalized) throw new Error(`${name} is required.`);
  return normalized;
}

async function finishOrFalse(operation: () => Promise<boolean>): Promise<boolean> {
  try {
    return await operation();
  } catch {
    // 完成写入异常与 CAS=0 都无法证明最终态已耐久化，统一进入恢复路径。
    return false;
  }
}

function recoveryRequired(): FulfilmentActionResult {
  return { state: "recovery-required", errorCode: "DURABILITY_CONFLICT" };
}
