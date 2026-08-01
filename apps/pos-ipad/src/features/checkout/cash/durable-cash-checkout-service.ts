import type {
  CashCheckoutDependencies,
  CashDrawerDisposition,
  CashCheckoutInput,
  CashCheckoutResult,
  LocalSequencePort,
} from "./cash-checkout-service";

import {
  auditActorPayload,
  createAud,
  type CashFulfilmentDraft,
  type CompleteCashOrderCommand,
  type DurableCashOrderCommitPort,
  type LocalOrder,
  type RecalledHoldCompletion,
  type TerminalCheckoutContext,
} from "@/core/contracts";
import { calculateCashSettlement } from "@/features/sales/domain";

export interface DurableCashFulfilmentPlannerPort {
  /** 先在提交前冻结履约计划；SQLCipher 事务只消费不可变草稿。 */
  createDraft(command: CompleteCashOrderCommand): Promise<DurableCashFulfilmentPlan>;
}

export type PlannedCashDrawerDisposition = Exclude<
  CashDrawerDisposition,
  "replayed"
>;

export type DurableCashFulfilmentPlan = Readonly<{
  draft: CashFulfilmentDraft;
  drawerDisposition: PlannedCashDrawerDisposition;
}>;

type IntentFlight = Readonly<{
  requestSignature: string;
  promise: Promise<CashCheckoutResult>;
}>;

export type DurableCashCheckoutDependencies = CashCheckoutDependencies & LocalSequencePort;

export interface TerminalCheckoutContextResolverPort {
  /**
   * 组合根必须用确认时的购物车快照校验它仍是共享 terminal session 的当前快照。
   * 页面和 CashCheckoutInput 都不能传入或覆盖取单 binding。
   */
  resolve(
    cart: CashCheckoutInput["cart"],
  ): TerminalCheckoutContext | Promise<TerminalCheckoutContext>;
}

const noTerminalCheckoutContext: TerminalCheckoutContextResolverPort = {
  resolve: () => ({ kind: "none" }),
};

/**
 * checkoutIntent 的持久幂等现金提交。
 *
 * 内存映射只负责同一服务实例内的单飞和快速签名冲突；跨进程/重启的最终事实由
 * DurableCashOrderCommitPort 的 SQLCipher 独占事务保存并返回，绝不依赖设备时钟。
 */
export class DurableCashCheckoutService {
  private readonly byIntent = new Map<string, IntentFlight>();

  public constructor(
    private readonly committer: DurableCashOrderCommitPort,
    private readonly planner: DurableCashFulfilmentPlannerPort,
    private readonly deps: DurableCashCheckoutDependencies,
    private readonly terminalContextResolver: TerminalCheckoutContextResolverPort =
      noTerminalCheckoutContext,
  ) {}

  public async complete(input: CashCheckoutInput): Promise<CashCheckoutResult> {
    if (!input.checkoutIntentId.trim()) {
      throw new Error("checkoutIntentId is required.");
    }

    // 中文注释：先从共享 terminal session 冻结上下文，再参与单飞签名；resolver
    // 失败时尚未规划、分配订单或触碰 SQLCipher。
    const terminalContext = normalizeTerminalCheckoutContext(
      await this.terminalContextResolver.resolve(input.cart),
      input,
    );
    const requestSignature = createRequestSignature(input, terminalContext);
    const existing = this.byIntent.get(input.checkoutIntentId);
    if (existing) {
      if (existing.requestSignature !== requestSignature) {
        throw new Error("checkoutIntentId request signature mismatch.");
      }
      return existing.promise;
    }

    let pending!: Promise<CashCheckoutResult>;
    pending = this.completeOnce(
      input,
      requestSignature,
      terminalContext,
    ).catch((error: unknown) => {
      // 中文注释：失败没有可清空购物车的结果，移除单飞记录后允许同一内容在修复介质后重试。
      if (this.byIntent.get(input.checkoutIntentId)?.promise === pending) {
        this.byIntent.delete(input.checkoutIntentId);
      }
      throw error;
    });
    this.byIntent.set(input.checkoutIntentId, { requestSignature, promise: pending });
    return pending;
  }

  private async completeOnce(
    input: CashCheckoutInput,
    requestSignature: string,
    terminalContext: TerminalCheckoutContext,
  ): Promise<CashCheckoutResult> {
    validateCashInput(input);
    const actual = input.cart.actualAmount.cents;
    const cashTendered = input.cashTenderedCents ?? 0;
    const settlement = calculateCashSettlement({
      actualAmount: createAud(actual),
      cashTendered: createAud(cashTendered),
    });
    assertSufficientTender(actual, input.cashTenderedCents, settlement.cashDue.cents, settlement.normalizedCashTendered.cents);

    if (input.cart.mode === "return") {
      const validReturn = input.cart.lines.every(
        (line) => line.kind === "return" && line.returnSourceKey && line.originalOrderGuid,
      );
      if (!validReturn || !(await this.deps.returnCapacity(input.cart))) {
        throw new Error("Offline return capacity is unknown or exhausted.");
      }
    }

    const orderGuid = requiredId(this.deps.createId(), "order id");
    const soldAtIso = this.deps.nowIso();
    const localSequence = await this.deps.nextLocalSequence();
    if (!Number.isSafeInteger(localSequence) || localSequence <= 0) {
      throw new Error("local sequence must be a positive safe integer.");
    }
    const planningCommand = createCommand(
      input,
      orderGuid,
      soldAtIso,
      localSequence,
      settlement.cashDue.cents,
      settlement.change.cents,
      this.deps.createId,
    );
    const plan = await this.planner.createDraft(planningCommand);
    validateFulfilmentPlan(planningCommand, plan);
    const fulfilment = plan.draft;
    const command = finalizeFulfilmentPolicy(planningCommand, fulfilment);
    const recalledHoldCompletion = createRecalledHoldCompletion(
      terminalContext,
      command,
      soldAtIso,
      input,
      this.deps.createId,
    );
    const persisted = await this.committer.completeDurableCashOrder({
      intent: {
        checkoutIntentId: input.checkoutIntentId,
        requestSignature,
        cashDueCents: settlement.cashDue.cents,
        changeCents: settlement.change.cents,
      },
      command,
      fulfilment,
      terminalContext,
      recalledHoldCompletion,
    });

    return {
      completed: true,
      canClearCart: true,
      orderGuid: persisted.orderGuid,
      cashDueCents: persisted.cashDueCents,
      changeCents: persisted.changeCents,
      // 中文注释：重放只复用账本结果，绝不能再次发出钱箱脉冲。
      postCommit: {
        requestDrawer:
          !persisted.replayed &&
          command.requiresDrawer &&
          plan.drawerDisposition === "queued",
        drawerDisposition: persisted.replayed
          ? "replayed"
          : plan.drawerDisposition,
        printPolicy: command.printPolicy,
      },
    };
  }
}

function validateFulfilmentPlan(
  command: CompleteCashOrderCommand,
  plan: DurableCashFulfilmentPlan,
): void {
  const hasDrawer = plan.draft.drawer !== null;
  if (plan.drawerDisposition === "queued" && !hasDrawer) {
    throw new Error("Queued cash drawer disposition requires a durable drawer event.");
  }
  if (plan.drawerDisposition !== "queued" && hasDrawer) {
    throw new Error("Cash drawer event requires queued disposition.");
  }
  if (!command.requiresDrawer && plan.drawerDisposition !== "not-required") {
    throw new Error("Zero cash order must use not-required drawer disposition.");
  }
  if (command.requiresDrawer && plan.drawerDisposition === "not-required") {
    throw new Error("Cash order requiring a drawer cannot use not-required disposition.");
  }
}

function finalizeFulfilmentPolicy(
  command: CompleteCashOrderCommand,
  fulfilment: CashFulfilmentDraft,
): CompleteCashOrderCommand & Readonly<{ printPolicy: "automatic" | "never" }> {
  return {
    ...command,
    printPolicy: fulfilment.print === null ? "never" : "automatic",
    requiresDrawer: fulfilment.drawer !== null,
  };
}

function createCommand(
  input: CashCheckoutInput,
  orderGuid: string,
  soldAtIso: string,
  localSequence: number,
  cashDueCents: number,
  changeCents: number,
  createId: () => string,
): CompleteCashOrderCommand {
  const actual = input.cart.actualAmount.cents;
  const order: LocalOrder = {
    orderGuid,
    localSequence,
    storeCode: input.storeCode,
    deviceCode: input.deviceCode,
    cashierId: input.cashierId,
    cashierName: input.cashierName,
    soldAtIso,
    state: "PendingSync",
    total: input.cart.subtotal,
    discount: input.cart.discount,
    actualAmount: input.cart.actualAmount,
    lines: input.cart.lines,
    tenders: actual === 0
      ? []
      : [{ tenderGuid: requiredId(createId(), "tender id"), method: "cash", amount: createAud(actual), reference: null, reservationToken: null }],
    originalOrderGuid: input.cart.lines.find((line) => line.originalOrderGuid)?.originalOrderGuid ?? null,
  };
  return {
    order,
    auditEvents: [{
      eventId: requiredId(createId(), "audit id"),
      eventType: actual < 0 ? "RETURN_REFUND_COMPLETE" : "SALE_COMPLETE",
      occurredAtIso: soldAtIso,
      orderGuid,
      correlationId: orderGuid,
      // 预渲染小票从同一耐久命令读取取整应收与找零，不能再根据浮动 UI 状态重算。
      payload: {
        checkoutIntentId: input.checkoutIntentId,
        localSequence,
        cashDueCents,
        changeCents,
        ...auditActorPayload(input),
      },
    }],
    outbox: {
      messageId: requiredId(createId(), "outbox id"),
      aggregateId: orderGuid,
      kind: "order-sync",
      payloadJson: JSON.stringify({ orderGuid }),
      nextAttemptAtIso: soldAtIso,
    },
    // prompt 仅表示规划阶段可按真实门店设置选择；提交前会固化为 automatic/never。
    requiresDrawer: actual !== 0,
    printPolicy: "prompt",
  };
}

function validateCashInput(input: CashCheckoutInput): void {
  if (!input.cart.lines.length) throw new Error("Cash checkout requires cart lines.");
  assertAudCents(input.cart.subtotal.cents, "cart subtotal");
  assertAudCents(input.cart.discount.cents, "cart discount");
  assertAudCents(input.cart.actualAmount.cents, "cart actual amount");
  if (input.cashTenderedCents !== null) assertAudCents(input.cashTenderedCents, "cash tendered");
  for (const line of input.cart.lines) {
    assertAudCents(line.unitPrice.cents, "line unit price");
    assertAudCents(line.discount.cents, "line discount");
    assertAudCents(line.actualAmount.cents, "line actual amount");
  }
}

function assertSufficientTender(
  actual: number,
  tendered: number | null,
  cashDue: number,
  normalizedTendered: number,
): void {
  if (actual > 0 && (tendered === null || normalizedTendered < cashDue)) {
    throw new Error("Insufficient cash tendered.");
  }
  if (actual < 0 && (tendered === null || normalizedTendered > cashDue)) {
    throw new Error("Insufficient cash refund tendered.");
  }
  if (actual === 0 && tendered !== null && tendered !== 0) {
    throw new Error("Zero order cannot accept cash tender.");
  }
}

function createRequestSignature(
  input: CashCheckoutInput,
  terminalContext: TerminalCheckoutContext,
): string {
  return `cash-v2:${canonicalJson({
    cart: input.cart,
    cashTenderedCents: input.cashTenderedCents,
    storeCode: input.storeCode,
    deviceCode: input.deviceCode,
    cashierId: input.cashierId,
    cashierName: input.cashierName,
    userGuid: input.userGuid,
    terminalContext,
  })}`;
}

function normalizeTerminalCheckoutContext(
  value: TerminalCheckoutContext,
  input: CashCheckoutInput,
): TerminalCheckoutContext {
  if (!isRecord(value)) {
    throw new Error("Invalid terminal checkout context.");
  }
  if (value.kind === "none") {
    if (!hasExactKeys(value, ["kind"])) {
      throw new Error("Invalid terminal checkout context.");
    }
    return { kind: "none" };
  }
  if (
    value.kind !== "recalled" ||
    !hasExactKeys(value, ["kind", "scope", "holdId", "recallAttemptId"]) ||
    !isRecord(value.scope) ||
    !hasExactKeys(value.scope, ["storeCode", "deviceCode"])
  ) {
    throw new Error("Invalid terminal checkout context.");
  }

  const storeCode = requiredText(value.scope.storeCode, "terminal store code");
  const deviceCode = requiredText(value.scope.deviceCode, "terminal device code");
  if (storeCode !== input.storeCode || deviceCode !== input.deviceCode) {
    throw new Error("Terminal checkout context scope mismatch.");
  }
  return {
    kind: "recalled",
    scope: { storeCode, deviceCode },
    holdId: requiredText(value.holdId, "held order id"),
    recallAttemptId: requiredText(
      value.recallAttemptId,
      "recall attempt id",
    ),
  };
}

function createRecalledHoldCompletion(
  terminalContext: TerminalCheckoutContext,
  command: CompleteCashOrderCommand,
  soldAtIso: string,
  input: CashCheckoutInput,
  createId: () => string,
): RecalledHoldCompletion | null {
  if (terminalContext.kind === "none") return null;
  return {
    binding: terminalContext,
    recalledAtIso: soldAtIso,
    recallAudit: {
      eventId: requiredId(createId(), "recall audit id"),
      eventType: "ORDER_RECALL",
      occurredAtIso: soldAtIso,
      orderGuid: command.order.orderGuid,
      correlationId: terminalContext.holdId,
      // 中文注释：只记录成交对账所需汇总，绝不复制商品、条码、token 或顾客资料。
      payload: {
        source: "ipad-pos",
        action: "recall",
        result: "completed",
        storeCode: command.order.storeCode,
        deviceCode: command.order.deviceCode,
        cashierId: command.order.cashierId,
        ...auditActorPayload(input),
        itemCount: input.cart.lines.length,
        actualAmountCents: input.cart.actualAmount.cents,
        localSequence: command.order.localSequence,
      },
    },
  };
}

function canonicalJson(value: unknown): string {
  if (value === null) return "null";
  if (typeof value === "string" || typeof value === "boolean") return JSON.stringify(value);
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new Error("checkout intent contains a non-finite number.");
    return Object.is(value, -0) ? "0" : String(value);
  }
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  if (typeof value === "object") {
    const record = value as Record<string, unknown>;
    return `{${Object.keys(record).sort().map((key) => `${JSON.stringify(key)}:${canonicalJson(record[key])}`).join(",")}}`;
  }
  throw new Error("checkout intent contains an unsupported value.");
}

function assertAudCents(value: number, label: string): void {
  if (!Number.isSafeInteger(value)) throw new Error(`${label} must use integer cents.`);
}

function requiredId(value: string, label: string): string {
  if (!value.trim()) throw new Error(`${label} is required.`);
  return value;
}

function requiredText(value: unknown, label: string): string {
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(`${label} is required.`);
  }
  return value.trim();
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasExactKeys(
  value: Record<string, unknown>,
  expected: readonly string[],
): boolean {
  const actual = Object.keys(value).sort();
  const wanted = [...expected].sort();
  return (
    actual.length === wanted.length &&
    actual.every((key, index) => key === wanted[index])
  );
}
