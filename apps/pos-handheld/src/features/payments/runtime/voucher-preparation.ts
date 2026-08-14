import type { PaymentAttempt, PaymentOperation } from "@/core/contracts";
import type {
  VoucherPaymentContext,
  VoucherPaymentContextProvider,
} from "@/features/payments/voucher/voucher-payment-adapter";

export type VoucherPreparationIdentity = Readonly<{
  storeCode: string;
  cashierId: string;
}>;

export interface VoucherPreparationIdentityPort {
  /**
   * 只能从当前可信设备/收银员会话解析；页面输入不得覆盖门店或收银员身份。
   */
  resolve(): Promise<VoucherPreparationIdentity>;
}

export type VoucherPurchasePreparationInput = Readonly<{
  actionId: string;
  orderGuid: string;
  voucherCode: string;
}>;

export type VoucherRefundPreparationInput = Readonly<{
  actionId: string;
  orderGuid: string;
  refundReason: string;
}>;

export type VoucherPreparedContextDraft = Readonly<{
  actionId: string;
  orderGuid: string;
  operation: PaymentOperation;
  storeCode: string;
  cashierId: string;
  voucherCode: string | null;
  refundReason: string | null;
}>;

export type VoucherPreparedContext = VoucherPreparedContextDraft &
  Readonly<{
    protectedReference: string;
    attemptId: string | null;
    idempotencyKey: string | null;
  }>;

export type VoucherPreparedAttemptBinding = Readonly<{
  orderGuid: string;
  operation: PaymentOperation;
  attemptId: string;
  idempotencyKey: string;
}>;

export interface VoucherPreparationStorePort {
  /**
   * 必须在 SQLCipher/Keychain 等受保护存储中按 (orderGuid, actionId) 原子保存或返回。
   * 同一 action 的券码、操作或可信身份发生变化时必须拒绝，不能覆盖。
   */
  prepare(input: VoucherPreparedContextDraft): Promise<string>;

  /**
   * provider 调用前把已准备上下文原子绑定到唯一 attempt。响应丢失后相同 attempt
   * 必须仍解析到同一上下文；不同 attempt 不得抢占。
   */
  bindToAttempt(
    input: VoucherPreparedAttemptBinding,
  ): Promise<VoucherPreparedContext | null>;
}

export interface VoucherPreparationSessionGuard {
  assertActive(): void | Promise<void>;
}

export type VoucherPreparationResult = Readonly<{
  prepared: true;
}>;

/**
 * 券码和退款原因只在受保护存储与 Voucher adapter 内部流动。
 * 返回值故意不携带 protectedReference、券码或 reservation token。
 */
export class DurableVoucherPreparationService {
  public constructor(
    private readonly store: VoucherPreparationStorePort,
    private readonly identity: VoucherPreparationIdentityPort,
    private readonly session: VoucherPreparationSessionGuard,
  ) {}

  public async preparePurchase(
    input: VoucherPurchasePreparationInput,
  ): Promise<VoucherPreparationResult> {
    await this.assertActive();
    const identity = await this.resolveIdentity();
    await this.assertActive();
    await this.store.prepare({
      actionId: requiredText(input.actionId, "VOUCHER_ACTION_ID_REQUIRED"),
      orderGuid: requiredText(input.orderGuid, "VOUCHER_ORDER_GUID_REQUIRED"),
      operation: "purchase",
      storeCode: identity.storeCode,
      cashierId: identity.cashierId,
      voucherCode: requiredText(input.voucherCode, "VOUCHER_CODE_REQUIRED"),
      refundReason: null,
    });
    await this.assertActive();
    return { prepared: true };
  }

  public async prepareRefund(
    input: VoucherRefundPreparationInput,
  ): Promise<VoucherPreparationResult> {
    await this.assertActive();
    const identity = await this.resolveIdentity();
    await this.assertActive();
    await this.store.prepare({
      actionId: requiredText(input.actionId, "VOUCHER_ACTION_ID_REQUIRED"),
      orderGuid: requiredText(input.orderGuid, "VOUCHER_ORDER_GUID_REQUIRED"),
      operation: "refund",
      storeCode: identity.storeCode,
      cashierId: identity.cashierId,
      voucherCode: null,
      refundReason: requiredText(
        input.refundReason,
        "VOUCHER_REFUND_REASON_REQUIRED",
      ),
    });
    await this.assertActive();
    return { prepared: true };
  }

  /**
   * 该 provider 只读取预先耐久准备的上下文，不会从页面闭包临时取券码。
   */
  public readonly contextForAttempt: VoucherPaymentContextProvider = async (
    attempt,
  ) => {
    await this.assertActive();
    assertAttemptIdentity(attempt);
    const prepared = await this.store.bindToAttempt({
      orderGuid: attempt.orderGuid,
      operation: attempt.operation,
      attemptId: attempt.attemptId,
      idempotencyKey: attempt.idempotencyKey,
    });
    await this.assertActive();
    if (!prepared) {
      throw codedError(
        "VOUCHER_CONTEXT_NOT_PREPARED",
        "Voucher context was not durably prepared before provider invocation.",
      );
    }
    assertPreparedAttempt(prepared, attempt);
    return {
      storeCode: prepared.storeCode,
      cashierId: prepared.cashierId,
      voucherCode: prepared.voucherCode,
      refundReason: prepared.refundReason,
    } satisfies VoucherPaymentContext;
  };

  private async resolveIdentity(): Promise<VoucherPreparationIdentity> {
    const identity = await this.identity.resolve();
    return {
      storeCode: requiredText(
        identity.storeCode,
        "VOUCHER_STORE_CODE_REQUIRED",
      ),
      cashierId: requiredText(
        identity.cashierId,
        "VOUCHER_CASHIER_ID_REQUIRED",
      ),
    };
  }

  private async assertActive(): Promise<void> {
    await this.session.assertActive();
  }
}

function assertAttemptIdentity(attempt: PaymentAttempt): void {
  requiredText(attempt.attemptId, "VOUCHER_ATTEMPT_ID_REQUIRED");
  requiredText(attempt.idempotencyKey, "VOUCHER_IDEMPOTENCY_KEY_REQUIRED");
  requiredText(attempt.orderGuid, "VOUCHER_ORDER_GUID_REQUIRED");
}

function assertPreparedAttempt(
  prepared: VoucherPreparedContext,
  attempt: PaymentAttempt,
): void {
  if (
    prepared.orderGuid !== attempt.orderGuid ||
    prepared.operation !== attempt.operation ||
    prepared.attemptId !== attempt.attemptId ||
    prepared.idempotencyKey !== attempt.idempotencyKey
  ) {
    throw codedError(
      "VOUCHER_CONTEXT_BINDING_CONFLICT",
      "Prepared voucher context belongs to another immutable attempt.",
    );
  }
  requiredText(prepared.protectedReference, "VOUCHER_CONTEXT_REFERENCE_REQUIRED");
  requiredText(prepared.storeCode, "VOUCHER_STORE_CODE_REQUIRED");
  requiredText(prepared.cashierId, "VOUCHER_CASHIER_ID_REQUIRED");
  if (attempt.operation === "purchase") {
    requiredText(prepared.voucherCode ?? "", "VOUCHER_CODE_REQUIRED");
    if (prepared.refundReason !== null) {
      throw codedError(
        "VOUCHER_CONTEXT_BINDING_CONFLICT",
        "Purchase voucher context contains refund-only data.",
      );
    }
  } else {
    requiredText(
      prepared.refundReason ?? "",
      "VOUCHER_REFUND_REASON_REQUIRED",
    );
    if (prepared.voucherCode !== null) {
      throw codedError(
        "VOUCHER_CONTEXT_BINDING_CONFLICT",
        "Refund voucher context contains purchase-only data.",
      );
    }
  }
}

function requiredText(value: string, code: string): string {
  const normalized = value.trim();
  if (!normalized) throw codedError(code, code);
  return normalized;
}

function codedError(code: string, message: string): Error & { code: string } {
  return Object.assign(new Error(message), { code });
}
