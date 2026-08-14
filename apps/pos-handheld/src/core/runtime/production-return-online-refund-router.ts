import type {
  DurableOfflineCashRefundPort,
  DurableOnlineReturnRefundPort,
  OfflineCashRefundInput,
  OnlineReturnRefundInput,
  PreparedOnlineReturnAttempt,
  ReturnAllocationExternalOutcome,
} from "@/features/returns/adapters/durable-return-execution-orchestrator";

export type ProductionReturnOnlineRefundRouterOptions = Readonly<{
  providerRefund: DurableOnlineReturnRefundPort | null;
}>;

/**
 * 在线现金退款没有支付 provider 副作用：workflow 已完成联网门禁，现金事实会由
 * return ledger、订单 outbox 与后续钱箱计划同事务落库。因此这里仅把已耐久保存的
 * externalAttemptId 绑定回 allocation，绝不在 prepare/submit/recover 中开钱箱。
 */
export class ProductionReturnOnlineRefundRouter
  implements DurableOnlineReturnRefundPort
{
  public constructor(
    private readonly options: ProductionReturnOnlineRefundRouterOptions,
  ) {}

  public async prepareAttempt(
    input: Omit<
      OnlineReturnRefundInput,
      "attemptKind" | "externalActionId" | "durableAttemptId"
    >,
  ): Promise<PreparedOnlineReturnAttempt> {
    if (input.method !== "cash") {
      return this.requireProvider(input.method).prepareAttempt(input);
    }
    validateCashPreparation(input);
    const attemptId = requiredText(input.externalAttemptId);
    return Object.freeze({
      attemptKind: "hbpos-api" as const,
      externalActionId: attemptId,
      durableAttemptId: attemptId,
    });
  }

  public async submit(
    input: OnlineReturnRefundInput,
  ): Promise<ReturnAllocationExternalOutcome> {
    if (input.method !== "cash") {
      return this.requireProvider(input.method).submit(input);
    }
    assertCashBinding(input);
    return Object.freeze({ status: "completed" as const });
  }

  public async recover(
    input: OnlineReturnRefundInput &
      Readonly<{ protectedRecoveryKey: string | null }>,
  ): Promise<ReturnAllocationExternalOutcome> {
    if (input.method !== "cash") {
      return this.requireProvider(input.method).recover(input);
    }
    assertCashBinding(input);
    if (input.protectedRecoveryKey !== null) {
      throw new ReturnOnlineRefundRouterError(
        "RETURN_CASH_RECOVERY_KEY_INVALID",
      );
    }
    return Object.freeze({ status: "completed" as const });
  }

  private requireProvider(method: OnlineReturnRefundInput["method"]) {
    if (method !== "card" && method !== "voucher") {
      throw new ReturnOnlineRefundRouterError(
        "RETURN_REFUND_METHOD_UNSUPPORTED",
      );
    }
    if (!this.options.providerRefund) {
      throw new ReturnOnlineRefundRouterError(
        "RETURN_PROVIDER_REFUND_UNAVAILABLE",
      );
    }
    return this.options.providerRefund;
  }
}

export class ProductionReturnCashRefundAdapter
  implements DurableOfflineCashRefundPort
{
  public async submit(
    input: OfflineCashRefundInput,
  ): Promise<ReturnAllocationExternalOutcome> {
    validateOfflineCashProof(input);
    return Object.freeze({ status: "completed" as const });
  }

  public async recover(
    input: OfflineCashRefundInput &
      Readonly<{ protectedRecoveryKey: string | null }>,
  ): Promise<ReturnAllocationExternalOutcome> {
    validateOfflineCashProof(input);
    if (input.protectedRecoveryKey !== null) {
      throw new ReturnOnlineRefundRouterError(
        "RETURN_CASH_RECOVERY_KEY_INVALID",
      );
    }
    return Object.freeze({ status: "completed" as const });
  }
}

export class ReturnOnlineRefundRouterError extends Error {
  public constructor(public readonly code: string) {
    super(code);
    this.name = "ReturnOnlineRefundRouterError";
  }
}

function validateCashPreparation(
  input: Omit<
    OnlineReturnRefundInput,
    "attemptKind" | "externalActionId" | "durableAttemptId"
  >,
): void {
  requiredText(input.actionId);
  requiredText(input.allocationId);
  requiredText(input.externalAttemptId);
  requiredText(input.returnOrderGuid);
  if (
    !Number.isSafeInteger(input.signedAmountCents) ||
    input.signedAmountCents >= 0
  ) {
    throw new ReturnOnlineRefundRouterError(
      "RETURN_CASH_AMOUNT_INVALID",
    );
  }
  const capacityId = nullableText(input.capacityId);
  const originalOrderGuid = nullableText(input.originalOrderGuid);
  if ((capacityId === null) !== (originalOrderGuid === null)) {
    throw new ReturnOnlineRefundRouterError(
      "RETURN_CASH_SOURCE_MISMATCH",
    );
  }
}

function assertCashBinding(input: OnlineReturnRefundInput): void {
  validateCashPreparation(input);
  const expected = requiredText(input.externalAttemptId);
  if (
    input.attemptKind !== "hbpos-api" ||
    requiredText(input.externalActionId) !== expected ||
    requiredText(input.durableAttemptId) !== expected
  ) {
    throw new ReturnOnlineRefundRouterError(
      "RETURN_CASH_ATTEMPT_MISMATCH",
    );
  }
}

function validateOfflineCashProof(input: OfflineCashRefundInput): void {
  requiredText(input.actionId);
  requiredText(input.allocationId);
  requiredText(input.returnOrderGuid);
  const originalOrderGuid = requiredText(input.originalOrderGuid);
  const capacityId = requiredText(input.capacityId);
  const proof = input.offlineCashProof;
  const magnitude = -input.signedAmountCents;
  if (
    !Number.isSafeInteger(input.signedAmountCents) ||
    input.signedAmountCents >= 0 ||
    !Number.isSafeInteger(magnitude) ||
    requiredText(proof.evidenceId).length === 0 ||
    requiredText(proof.capacityId) !== capacityId ||
    requiredText(proof.originalOrderGuid) !== originalOrderGuid ||
    !Number.isSafeInteger(proof.remainingCents) ||
    proof.remainingCents < magnitude
  ) {
    throw new ReturnOnlineRefundRouterError(
      "RETURN_OFFLINE_CASH_PROOF_MISMATCH",
    );
  }
}

function requiredText(value: unknown): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new ReturnOnlineRefundRouterError(
      "RETURN_CASH_ATTEMPT_MISMATCH",
    );
  }
  return value.trim();
}

function nullableText(value: unknown): string | null {
  if (value === null) return null;
  return requiredText(value);
}
