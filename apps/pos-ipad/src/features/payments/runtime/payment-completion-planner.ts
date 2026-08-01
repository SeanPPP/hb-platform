import type {
  AuditActorSnapshot,
  AuditEventDraft,
  CashFulfilmentDraft,
  Money,
  OutboxMessageDraft,
  PaymentProvider,
} from "@/core/contracts";
import { auditActorPayload } from "@/core/contracts";
import type {
  ApprovedPaymentOrderCompletionPlan,
  ApprovedPaymentOrderCompletionPlannerPort,
} from "@/features/payments/approved-payment-order-completion";
import type { PaymentAttemptExecutionResult } from "@/features/payments/payment-attempt-service";

export type PaymentCompletionProjection = Readonly<{
  orderGuid: string;
  total: Money;
  paid: Money;
}>;

export interface PaymentCompletionProjectionPort {
  /** 必须读取当前 SQLCipher order+tender truth，不能使用页面剩余金额。 */
  read(orderGuid: string): Promise<PaymentCompletionProjection | null>;
}

export type PaymentCompletionMethod = "cash" | "card" | "voucher";

export type PaymentCompletionSettings = Readonly<{
  printerId: string | null;
  automaticPrint: Readonly<Record<PaymentCompletionMethod, boolean>>;
  cashDrawerEnabled: boolean;
  /** 必须是当前收银员已解析的 CashDrawer.Open 权限，不允许页面传入。 */
  cashDrawerPermissionAllowed: boolean;
}>;

export interface PaymentCompletionSettingsPort {
  load(): Promise<PaymentCompletionSettings | null>;
}

export type PaymentReceiptRenderInput = Readonly<{
  orderGuid: string;
  method: PaymentCompletionMethod;
  amount: Money;
  attemptId: string | null;
}>;

export interface PaymentReceiptRendererPort {
  /**
   * renderer 由 orderGuid 自行读取完整订单；这里不传 provider receipt、references、
   * token、券码或收银员凭据。
   */
  render(input: PaymentReceiptRenderInput): Promise<Uint8Array>;
}

export type PaymentFinalCompletionPlan = Readonly<{
  completionAuditEvents: readonly AuditEventDraft[];
  outbox: OutboxMessageDraft;
  fulfilment: CashFulfilmentDraft;
}>;

export type MixedCashFinalCompletionInput = Readonly<{
  actionId: string;
  orderGuid: string;
  amount: Money;
  expectedRemaining: Money;
  actor: AuditActorSnapshot;
}>;

export interface MixedCashOrderCompletionPlannerPort {
  planFinalCash(
    input: MixedCashFinalCompletionInput,
  ): Promise<PaymentFinalCompletionPlan>;
}

export type PaymentCompletionPlannerDependencies = Readonly<{
  settings: PaymentCompletionSettingsPort;
  renderer: PaymentReceiptRendererPort;
  createId(): string;
  nowIso(): string;
}>;

/**
 * 生成最终完成才会由 DB 原子消费的审计、order-sync 与冻结外设草稿。
 * 外设动作永远不在 planner 内执行；renderer/settings 失败只降级为不排打印，
 * 不能让已批准银行卡交易因为打印配置阻塞订单落账。
 */
export class PaymentFinalCompletionPlanner
  implements MixedCashOrderCompletionPlannerPort
{
  public constructor(
    private readonly dependencies: PaymentCompletionPlannerDependencies,
  ) {}

  public async planFinalCash(
    input: MixedCashFinalCompletionInput,
  ): Promise<PaymentFinalCompletionPlan> {
    assertPositiveAud(input.amount, "MIXED_CASH_AMOUNT_INVALID");
    assertPositiveAud(
      input.expectedRemaining,
      "MIXED_CASH_REMAINING_INVALID",
    );
    if (
      input.amount.currency !== input.expectedRemaining.currency ||
      input.amount.cents !== input.expectedRemaining.cents
    ) {
      throw new Error(
        "MIXED_CASH_FINAL_AMOUNT_MUST_EQUAL_EXPECTED_REMAINING",
      );
    }
    return this.planFinal({
      orderGuid: requiredText(input.orderGuid, "ORDER_GUID_REQUIRED"),
      correlationId: requiredText(input.actionId, "ACTION_ID_REQUIRED"),
      method: "cash",
      amount: input.amount,
      attemptId: null,
      provider: null,
      eventType: "PAYMENT_MIXED_CASH_COMPLETE",
      allowDrawer: true,
      actor: input.actor,
    });
  }

  public async planApproved(
    input: Readonly<{
      orderGuid: string;
      attemptId: string;
      provider: PaymentProvider;
      amount: Money;
      actor: AuditActorSnapshot;
    }>,
  ): Promise<PaymentFinalCompletionPlan> {
    assertPositiveAud(input.amount, "APPROVED_PAYMENT_AMOUNT_INVALID");
    return this.planFinal({
      orderGuid: requiredText(input.orderGuid, "ORDER_GUID_REQUIRED"),
      correlationId: requiredText(input.attemptId, "ATTEMPT_ID_REQUIRED"),
      method: input.provider === "voucher" ? "voucher" : "card",
      amount: input.amount,
      attemptId: input.attemptId,
      provider: input.provider,
      eventType: "PAYMENT_APPROVED_COMPLETE",
      allowDrawer: false,
      actor: input.actor,
    });
  }

  private async planFinal(input: {
    orderGuid: string;
    correlationId: string;
    method: PaymentCompletionMethod;
    amount: Money;
    attemptId: string | null;
    provider: PaymentProvider | null;
    eventType: string;
    allowDrawer: boolean;
    actor: AuditActorSnapshot;
  }): Promise<PaymentFinalCompletionPlan> {
    const occurredAtIso = requiredIso(this.dependencies.nowIso());
    const settings = await safelyLoadSettings(this.dependencies.settings);
    const printerId = normalizedText(settings?.printerId ?? "");
    const print = await this.planPrint(input, settings, printerId);
    const drawer =
      input.allowDrawer &&
      settings?.cashDrawerEnabled === true &&
      settings.cashDrawerPermissionAllowed === true &&
      printerId
        ? {
            eventId: requiredText(
              this.dependencies.createId(),
              "DRAWER_EVENT_ID_REQUIRED",
            ),
            orderGuid: input.orderGuid,
            printerId,
            printJobId: print?.jobId ?? null,
            reason: "mixed-cash-sale",
          }
        : null;
    const auditPayload: Readonly<Record<string, unknown>> = Object.freeze({
      attemptId: input.attemptId,
      provider: input.provider,
      method: input.method,
      amountCents: input.amount.cents,
      printPlanned: print !== null,
      drawerPlanned: drawer !== null,
      ...auditActorPayload(input.actor),
    });
    return {
      completionAuditEvents: [
        {
          eventId: requiredText(
            this.dependencies.createId(),
            "AUDIT_EVENT_ID_REQUIRED",
          ),
          eventType: input.eventType,
          occurredAtIso,
          orderGuid: input.orderGuid,
          correlationId: input.correlationId,
          payload: auditPayload,
        },
      ],
      outbox: {
        messageId: requiredText(
          this.dependencies.createId(),
          "OUTBOX_MESSAGE_ID_REQUIRED",
        ),
        aggregateId: input.orderGuid,
        kind: "order-sync",
        payloadJson: JSON.stringify({ orderGuid: input.orderGuid }),
        nextAttemptAtIso: occurredAtIso,
      },
      fulfilment: { print, drawer },
    };
  }

  private async planPrint(
    input: {
      orderGuid: string;
      method: PaymentCompletionMethod;
      amount: Money;
      attemptId: string | null;
    },
    settings: PaymentCompletionSettings | null,
    printerId: string | null,
  ): Promise<CashFulfilmentDraft["print"]> {
    if (
      !settings ||
      !printerId ||
      settings.automaticPrint[input.method] !== true
    ) {
      return null;
    }
    try {
      const receiptBytes = await this.dependencies.renderer.render({
        orderGuid: input.orderGuid,
        method: input.method,
        amount: copyMoney(input.amount),
        attemptId: input.attemptId,
      });
      if (!(receiptBytes instanceof Uint8Array) || receiptBytes.length === 0) {
        return null;
      }
      return {
        jobId: requiredText(
          this.dependencies.createId(),
          "PRINT_JOB_ID_REQUIRED",
        ),
        orderGuid: input.orderGuid,
        printerId,
        receiptBytes: new Uint8Array(receiptBytes),
        isReprint: false,
      };
    } catch {
      return null;
    }
  }
}

export type ApprovedPaymentCompletionPlannerOptions = Readonly<{
  projection: PaymentCompletionProjectionPort;
  finalPlanner: PaymentFinalCompletionPlanner;
  createId(): string;
  nowIso(): string;
}>;

/**
 * Approved tender 的 projection 只决定是否预渲染最终履约。partial 时 DB 只写 tender
 * 并保持 Completing，不消费 audit/outbox/fulfilment；因此不调用 settings/renderer。
 * 若事务内 truth 与 projection 发生竞争，返回的惰性计划仍是合法、脱敏的 order-sync，
 * 但 fulfilment 保持空，绝不会误开钱箱。
 */
export class SafeApprovedPaymentCompletionPlanner
  implements ApprovedPaymentOrderCompletionPlannerPort
{
  public constructor(
    private readonly options: ApprovedPaymentCompletionPlannerOptions,
  ) {}

  public async plan(
    execution: PaymentAttemptExecutionResult,
    actor: AuditActorSnapshot,
  ): Promise<ApprovedPaymentOrderCompletionPlan> {
    const { attempt } = execution;
    if (attempt.state !== "Approved" || attempt.operation !== "purchase") {
      throw new Error("APPROVED_PURCHASE_REQUIRED");
    }
    const projection = await this.options.projection.read(attempt.orderGuid);
    if (!projection || projection.orderGuid !== attempt.orderGuid) {
      throw new Error("APPROVED_PAYMENT_ORDER_NOT_FOUND");
    }
    assertProjection(projection);
    const projectedPaid = safeAdd(projection.paid.cents, attempt.amount.cents);
    if (projectedPaid > projection.total.cents) {
      throw new Error("APPROVED_PAYMENT_WOULD_OVERPAY");
    }
    const tenderGuid = requiredText(
      this.options.createId(),
      "TENDER_GUID_REQUIRED",
    );
    if (projectedPaid < projection.total.cents) {
      const now = requiredIso(this.options.nowIso());
      return {
        tenderGuid,
        completionAuditEvents: [],
        // 接口字段在 partial 仍为必填；DB partial 分支不会插入该惰性计划。
        outbox: {
          messageId: requiredText(
            this.options.createId(),
            "OUTBOX_MESSAGE_ID_REQUIRED",
          ),
          aggregateId: attempt.orderGuid,
          kind: "order-sync",
          payloadJson: JSON.stringify({ orderGuid: attempt.orderGuid }),
          nextAttemptAtIso: now,
        },
        fulfilment: { print: null, drawer: null },
      };
    }
    const final = await this.options.finalPlanner.planApproved({
      orderGuid: attempt.orderGuid,
      attemptId: attempt.attemptId,
      provider: attempt.provider,
      amount: attempt.amount,
      actor,
    });
    return { tenderGuid, ...final };
  }
}

async function safelyLoadSettings(
  port: PaymentCompletionSettingsPort,
): Promise<PaymentCompletionSettings | null> {
  try {
    return await port.load();
  } catch {
    return null;
  }
}

function assertProjection(projection: PaymentCompletionProjection): void {
  if (
    projection.total.currency !== "AUD" ||
    projection.paid.currency !== "AUD" ||
    !Number.isSafeInteger(projection.total.cents) ||
    !Number.isSafeInteger(projection.paid.cents) ||
    projection.total.cents <= 0 ||
    projection.paid.cents < 0 ||
    projection.paid.cents > projection.total.cents
  ) {
    throw new Error("APPROVED_PAYMENT_PROJECTION_INVALID");
  }
}

function assertPositiveAud(amount: Money, code: string): void {
  if (
    amount.currency !== "AUD" ||
    !Number.isSafeInteger(amount.cents) ||
    amount.cents <= 0
  ) {
    throw new Error(code);
  }
}

function requiredText(value: string, code: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error(code);
  return normalized;
}

function normalizedText(value: string): string | null {
  const normalized = value.trim();
  return normalized || null;
}

function requiredIso(value: string): string {
  if (!Number.isFinite(Date.parse(value))) {
    throw new Error("TIMESTAMP_INVALID");
  }
  return new Date(value).toISOString();
}

function copyMoney(value: Money): Money {
  return { currency: value.currency, cents: value.cents };
}

function safeAdd(left: number, right: number): number {
  const value = left + right;
  if (!Number.isSafeInteger(value)) {
    throw new Error("PAYMENT_AMOUNT_OVERFLOW");
  }
  return value;
}
