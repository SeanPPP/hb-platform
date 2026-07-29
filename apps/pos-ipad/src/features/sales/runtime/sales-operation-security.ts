import type { AuditEventDraft, CartSnapshot } from "@/core/contracts";

export const SALES_OPERATION_NOT_AUTHORIZED =
  "SALES_OPERATION_NOT_AUTHORIZED";
export const SALES_CART_MUTATION_REJECTED =
  "SALES_CART_MUTATION_REJECTED";

export const SALES_PERMISSIONS = Object.freeze({
  view: "Permissions.PosTerminal.Sales.View",
  addItem: "Permissions.PosTerminal.Sales.AddItem",
  addOpenItem: "Permissions.PosTerminal.Sales.AddOpenItem",
  removeLine: "Permissions.PosTerminal.Sales.RemoveLine",
  changeQuantity: "Permissions.PosTerminal.Sales.ChangeQuantity",
  changePrice: "Permissions.PosTerminal.Sales.ChangePrice",
  lineManualDiscount:
    "Permissions.PosTerminal.Sales.LineManualDiscount",
  orderManualDiscount:
    "Permissions.PosTerminal.Sales.OrderManualDiscount",
  clearCart: "Permissions.PosTerminal.Sales.ClearCart",
} as const);

export type SalesAuthorizedOperationContext = Readonly<{
  authorizationMode: "current-cashier" | "offline-cache" | "online";
  requestingCashierId: string;
  authorizingCashierId: string | null;
  permissionCode: string;
}>;

export interface SalesOperationAuthorizationPort {
  authorizeAndRun<T>(
    input: Readonly<{
      actionId: string;
      permissionCode: string;
      screen: string;
      action: string;
    }>,
    operation: (
      context: SalesAuthorizedOperationContext,
    ) => T | Promise<T>,
  ): Promise<
    | Readonly<{ authorized: true; value: T }>
    | Readonly<{ authorized: false; reason: string }>
  >;
}

export type SalesOperationSecurity = Readonly<{
  authorization: SalesOperationAuthorizationPort;
  audit: Readonly<{
    append(events: readonly AuditEventDraft[]): Promise<void>;
  }>;
  createActionId(): string;
  createAuditEventId(): string;
  nowIso(): string;
}>;

export type SalesCartAuditEventType =
  | "CART_ITEM_ADD"
  | "CART_ITEM_REMOVE"
  | "CART_ITEM_QUANTITY_CHANGE"
  | "CART_ITEM_PRICE_CHANGE"
  | "CART_LINE_DISCOUNT_CHANGE"
  | "CART_ORDER_DISCOUNT_CHANGE"
  | "CART_CLEAR";

type SalesOperationIdentity = Readonly<{
  cashierId: string;
}>;

type SalesOperationSessionGuard = Readonly<{
  assertActive(): void;
}>;

type AuditAuthorizationContext = Readonly<{
  authorizationMode: string;
  requestingCashierId: string;
  authorizingCashierId: string | null;
  permissionCode: string;
}>;

/**
 * 销售动作只在授权 callback 内执行。主管条码和授权票据不会进入该类型，
 * 业务审计只保存 WPF 对齐的安全购物车差异。
 */
export class AuthorizedSalesOperationExecutor {
  public constructor(
    private readonly security: SalesOperationSecurity,
    private readonly identity: SalesOperationIdentity,
    private readonly sessionGuard: SalesOperationSessionGuard,
  ) {}

  public async runRead<T>(
    permissionCode: string,
    action: string,
    operation: () => T | Promise<T>,
  ): Promise<T> {
    const result = await this.authorize(permissionCode, action, () => {
      this.sessionGuard.assertActive();
      return operation();
    });
    if (!result.authorized) {
      throw codedError(
        SALES_OPERATION_NOT_AUTHORIZED,
        "Sales operation was not authorized.",
      );
    }
    return result.value;
  }

  public async runCartMutation<T>(
    input: Readonly<{
      permissionCode: string;
      action: string;
      eventType: SalesCartAuditEventType;
      getCart(): CartSnapshot;
      operation(): T | Promise<T>;
    }>,
  ): Promise<T> {
    const actionId = requiredText(
      this.security.createActionId(),
      "Sales action id",
    );
    const request = {
      actionId,
      permissionCode: requiredText(
        input.permissionCode,
        "Sales permission",
      ),
      screen: "pos-terminal",
      action: requiredText(input.action, "Sales action"),
    };
    const result = await this.security.authorization.authorizeAndRun(
      request,
      async (context) => {
        this.sessionGuard.assertActive();
        const before = input.getCart();
        try {
          const value = await input.operation();
          const after = input.getCart();
          if (cartChanged(before, after)) {
            await this.recordCartAudit({
              actionId,
              eventType: input.eventType,
              action: request.action,
              outcome: "Succeeded",
              reason: null,
              context,
              before,
              after,
            });
          }
          return value;
        } catch (error: unknown) {
          if (!hasCode(error, SALES_CART_MUTATION_REJECTED)) {
            await this.recordCartAudit({
              actionId,
              eventType: input.eventType,
              action: request.action,
              outcome: "Failed",
              reason: errorCode(error) ?? "UNEXPECTED_FAILURE",
              context,
              before,
              after: input.getCart(),
            });
          }
          throw error;
        }
      },
    );
    if (!result.authorized) {
      const current = input.getCart();
      await this.recordCartAudit({
        actionId,
        eventType: input.eventType,
        action: request.action,
        outcome: "Denied",
        reason: result.reason,
        context: {
          authorizationMode: "unavailable",
          requestingCashierId: this.identity.cashierId,
          authorizingCashierId: null,
          permissionCode: request.permissionCode,
        },
        before: current,
        after: current,
      });
      throw codedError(
        SALES_OPERATION_NOT_AUTHORIZED,
        "Sales operation was not authorized.",
      );
    }
    return result.value;
  }

  /**
   * 已授权动作的可信后续写入不再次请求主管授权，只按调用方声明的动作记录真实差异。
   * action/audit ID、时钟或仓储异常均在购物车变更后吞掉，不能反向回滚内存状态。
   */
  public async runTrustedCartMutation<T>(
    input: Readonly<{
      permissionCode: string;
      action: string;
      eventType: SalesCartAuditEventType;
      getCart(): CartSnapshot;
      operation(): T;
    }>,
  ): Promise<T> {
    this.sessionGuard.assertActive();
    const before = input.getCart();
    const value = input.operation();
    const after = input.getCart();
    if (!cartChanged(before, after)) return value;

    try {
      const actionId = requiredText(
        this.security.createActionId(),
        "Sales action id",
      );
      await this.recordCartAudit({
        actionId,
        eventType: input.eventType,
        action: requiredText(input.action, "Sales action"),
        outcome: "Succeeded",
        reason: null,
        context: {
          authorizationMode: "system",
          requestingCashierId: this.identity.cashierId,
          authorizingCashierId: null,
          permissionCode: requiredText(
            input.permissionCode,
            "Sales permission",
          ),
        },
        before,
        after,
      });
    } catch {
      // 可信目录回写已完成，任何审计基础设施异常均不能回滚购物车。
    }
    return value;
  }

  private authorize<T>(
    permissionCode: string,
    action: string,
    operation: (
      context: SalesAuthorizedOperationContext,
    ) => T | Promise<T>,
  ) {
    this.sessionGuard.assertActive();
    return this.security.authorization.authorizeAndRun(
      {
        actionId: requiredText(
          this.security.createActionId(),
          "Sales action id",
        ),
        permissionCode: requiredText(permissionCode, "Sales permission"),
        screen: "pos-terminal",
        action: requiredText(action, "Sales action"),
      },
      operation,
    );
  }

  private async recordCartAudit(input: Readonly<{
    actionId: string;
    eventType: SalesCartAuditEventType;
    action: string;
    outcome: "Succeeded" | "Failed" | "Denied";
    reason: string | null;
    context: AuditAuthorizationContext;
    before: CartSnapshot;
    after: CartSnapshot;
  }>): Promise<void> {
    try {
      const event: AuditEventDraft = {
        eventId: requiredText(
          this.security.createAuditEventId(),
          "Sales audit event id",
        ),
        eventType: input.eventType,
        occurredAtIso: canonicalIso(this.security.nowIso()),
        orderGuid: null,
        correlationId: input.actionId,
        payload: {
          outcome: input.outcome,
          action: input.action,
          screen: "pos-terminal",
          permissionCode: input.context.permissionCode,
          authorizationMode: input.context.authorizationMode,
          requestingCashierId: input.context.requestingCashierId,
          authorizingCashierId: input.context.authorizingCashierId,
          reason: safeReasonCode(input.reason),
          itemCount: input.after.lines.length,
          beforeSubtotalCents: input.before.subtotal.cents,
          afterSubtotalCents: input.after.subtotal.cents,
          beforeDiscountCents: input.before.discount.cents,
          afterDiscountCents: input.after.discount.cents,
          beforeActualCents: input.before.actualAmount.cents,
          afterActualCents: input.after.actualAmount.cents,
          amountDeltaCents:
            input.after.actualAmount.cents -
            input.before.actualAmount.cents,
          items:
            input.outcome === "Denied"
              ? unchangedCartItems(input.after)
              : cartDifferences(input.before, input.after),
        },
      };
      await this.security.audit.append([event]);
    } catch {
      // 与 WPF 一致：ID、时钟或仓储故障均不能回滚已经完成的购物车动作。
    }
  }
}

export function quickLineDiscountPermission(
  basisPoints: number,
): string | null {
  return quickDiscountPermission("Line", basisPoints);
}

export function quickOrderDiscountPermission(
  basisPoints: number,
): string | null {
  return quickDiscountPermission("Order", basisPoints);
}

export function cartMutationRejected(message: string): Error {
  return codedError(SALES_CART_MUTATION_REJECTED, message);
}

function quickDiscountPermission(
  scope: "Line" | "Order",
  basisPoints: number,
): string | null {
  if (
    basisPoints !== 1_000 &&
    basisPoints !== 2_000 &&
    basisPoints !== 3_000 &&
    basisPoints !== 4_000 &&
    basisPoints !== 5_000
  ) {
    return null;
  }
  return `Permissions.PosTerminal.Sales.${scope}QuickDiscount${basisPoints / 100}Percent`;
}

function cartChanged(before: CartSnapshot, after: CartSnapshot): boolean {
  return (
    before.subtotal.cents !== after.subtotal.cents ||
    before.discount.cents !== after.discount.cents ||
    before.actualAmount.cents !== after.actualAmount.cents ||
    cartDifferences(before, after).length > 0
  );
}

function cartDifferences(
  before: CartSnapshot,
  after: CartSnapshot,
): readonly Readonly<Record<string, unknown>>[] {
  const beforeById = new Map(before.lines.map((line) => [line.lineId, line]));
  const afterById = new Map(after.lines.map((line) => [line.lineId, line]));
  const lineIds = new Set([...beforeById.keys(), ...afterById.keys()]);
  const differences: Readonly<Record<string, unknown>>[] = [];
  for (const lineId of lineIds) {
    const previous = beforeById.get(lineId);
    const current = afterById.get(lineId);
    if (JSON.stringify(previous) === JSON.stringify(current)) continue;
    const difference = cartItemAudit(previous, current);
    if (difference) differences.push(difference);
  }
  return differences;
}

function unchangedCartItems(
  cart: CartSnapshot,
): readonly Readonly<Record<string, unknown>>[] {
  return cart.lines.flatMap((line) => {
    const item = cartItemAudit(line, line);
    return item ? [item] : [];
  });
}

function cartItemAudit(
  previous: CartSnapshot["lines"][number] | undefined,
  current: CartSnapshot["lines"][number] | undefined,
): Readonly<Record<string, unknown>> | null {
  const identity = current ?? previous;
  if (!identity) return null;
  const beforeQuantity = signedQuantity(previous);
  const afterQuantity = signedQuantity(current);
  const beforeUnitPriceCents = previous?.unitPrice.cents ?? 0;
  const afterUnitPriceCents = current?.unitPrice.cents ?? 0;
  const beforeDiscountCents = previous?.discount.cents ?? 0;
  const afterDiscountCents = current?.discount.cents ?? 0;
  const beforeGrossCents = grossCents(previous);
  const afterGrossCents = grossCents(current);
  const beforeActualCents = previous?.actualAmount.cents ?? 0;
  const afterActualCents = current?.actualAmount.cents ?? 0;
  return {
    productCode: identity.productCode,
    itemNumber: identity.itemNumber,
    referenceCode: identity.syncProvenance?.referenceCode ?? null,
    lookupCode: identity.lookupCode,
    displayName: identity.displayName,
    lineKind: identity.kind,
    beforeQuantity,
    afterQuantity,
    quantityDelta: afterQuantity - beforeQuantity,
    beforeUnitPriceCents,
    afterUnitPriceCents,
    unitPriceDeltaCents:
      afterUnitPriceCents - beforeUnitPriceCents,
    beforeDiscountCents,
    afterDiscountCents,
    discountDeltaCents:
      afterDiscountCents - beforeDiscountCents,
    beforeGrossCents,
    afterGrossCents,
    grossDeltaCents: afterGrossCents - beforeGrossCents,
    beforeActualCents,
    afterActualCents,
    actualDeltaCents: afterActualCents - beforeActualCents,
  };
}

function signedQuantity(
  line: CartSnapshot["lines"][number] | undefined,
): number {
  if (!line) return 0;
  const quantity = Number(line.quantity);
  return line.kind === "return" ? -quantity : quantity;
}

function grossCents(
  line: CartSnapshot["lines"][number] | undefined,
): number {
  if (!line) return 0;
  return signedQuantity(line) * line.unitPrice.cents;
}

function canonicalIso(value: string): string {
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) {
    throw new Error("Sales audit clock is invalid.");
  }
  return new Date(timestamp).toISOString();
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error(`${label} is required.`);
  return normalized;
}

function codedError(code: string, message: string): Error {
  return Object.assign(new Error(message), { code });
}

function hasCode(error: unknown, code: string): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    error.code === code
  );
}

function errorCode(error: unknown): string | null {
  if (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    typeof error.code === "string" &&
    error.code.length <= 64 &&
    /^[A-Z0-9_.-]+$/i.test(error.code)
  ) {
    return error.code;
  }
  return null;
}

function safeReasonCode(reason: string | null): string | null {
  if (reason === null) return null;
  return reason.length <= 64 && /^[A-Z0-9_.-]+$/i.test(reason)
    ? reason
    : "UNSAFE_REASON_REDACTED";
}
