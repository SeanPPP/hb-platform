import type { PaymentAttempt } from "@hb/pos-domain/core/contracts/payment";
import type { PaymentAttemptRepositoryPort } from "@hb/pos-domain/core/contracts/repositories";
import type { PaymentRecoveryControl } from "./payment-attempt-service";

export type PaymentAcknowledgementErrorCode =
  | "LINKLY_ACKNOWLEDGEMENT_PENDING";

export type PaymentAcknowledgementResult = Readonly<{
  attempt: PaymentAttempt;
  acknowledged: boolean;
  pending: boolean;
  errorCode: PaymentAcknowledgementErrorCode | null;
}>;

export interface LinklyPaymentAcknowledgementPort {
  acknowledge(attempt: PaymentAttempt): Promise<void>;
}

/** 仅 provider 可通过 active/resumable 的严格会话匹配产生该环境，不接受 UI 输入。 */
export interface LinklyLegacyAcknowledgementReconciler {
  reconcileLegacy(
    attempt: PaymentAttempt,
    control?: PaymentRecoveryControl,
  ): Promise<string | null>;
}

export type PaymentAcknowledgementLedgerPort = Pick<
  PaymentAttemptRepositoryPort,
  "get" | "markProviderAcknowledged" | "canProviderAcknowledged" | "verifyProviderEnvironment"
>;

export type PaymentAcknowledgementRuntimePort = Readonly<{
  acknowledge(attemptId: string): Promise<PaymentAcknowledgementResult>;
}>;

export class PaymentAcknowledgementNotFoundError extends Error {
  public constructor(attemptId: string) {
    super(`Payment attempt ${attemptId} was not found for acknowledgement.`);
    this.name = "PaymentAcknowledgementNotFoundError";
  }
}

/**
 * 已冻结环境的 Linkly ACK 只确认已耐久的最终交易并写入本地 marker，不查询、不
 * 重放扣款/退款，也不读取购物车。唯一例外是历史 NULL 环境：由 provider 做只读
 * 强匹配核验后冻结环境，仍不会重放金融操作。
 */
export class PaymentAcknowledgementService
  implements PaymentAcknowledgementRuntimePort
{
  private readonly inflight = new Map<string, Promise<PaymentAcknowledgementResult>>();
  public constructor(
    private readonly options: Readonly<{
      ledger: PaymentAcknowledgementLedgerPort;
      acknowledger: LinklyPaymentAcknowledgementPort;
      legacyReconciler?: LinklyLegacyAcknowledgementReconciler;
      nowIso(): string;
    }>,
  ) {}

  public acknowledge(attemptId: string): Promise<PaymentAcknowledgementResult> {
    const existing = this.inflight.get(attemptId);
    if (existing) return existing;
    const operation = this.acknowledgeOnce(attemptId);
    this.inflight.set(attemptId, operation);
    operation.finally(() => {
      if (this.inflight.get(attemptId) === operation) this.inflight.delete(attemptId);
    }).catch(() => undefined);
    return operation;
  }

  private async acknowledgeOnce(
    attemptId: string,
  ): Promise<PaymentAcknowledgementResult> {
    let attempt = await this.options.ledger.get(attemptId);
    if (!attempt) throw new PaymentAcknowledgementNotFoundError(attemptId);
    if (attempt.providerAcknowledgedAtIso) {
      return result(attempt, true, false, null);
    }
    if (!isLinklyFinal(attempt)) {
      return result(
        attempt,
        false,
        attempt.provider === "linkly-cloud",
        attempt.provider === "linkly-cloud"
          ? "LINKLY_ACKNOWLEDGEMENT_PENDING"
          : null,
      );
    }
    // 提交前明确拒绝或 Created 本地取消不会创建后端会话，无终端 guard 可确认。
    // 保留空 marker，不能伪造已收到服务端 ACK；Approved 缺少 session 仍须失败关闭。
    if ((attempt.state === "Declined" || attempt.state === "Cancelled") &&
        !attempt.references.sessionId?.trim()) {
      return result(attempt, true, false, null);
    }
    // 旧库在 M44 之前没有环境冻结。保留 pending/可见错误，绝不改用当前配置 ACK。
    if (!validEnvironment(attempt.providerEnvironment)) {
      const reconciler = this.options.legacyReconciler;
      const verifyEnvironment = this.options.ledger.verifyProviderEnvironment;
      if (!reconciler || !verifyEnvironment) {
        return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
      }
      let verifiedEnvironment: string | null;
      try {
        verifiedEnvironment = await reconciler.reconcileLegacy(attempt);
      } catch {
        return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
      }
      if (!validEnvironment(verifiedEnvironment)) {
        return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
      }
      try {
        if (!(await verifyEnvironment.call(this.options.ledger, attempt, verifiedEnvironment))) {
          return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
        }
        const refreshed = await this.options.ledger.get(attemptId);
        if (!refreshed || !validEnvironment(refreshed.providerEnvironment)) {
          return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
        }
        attempt = refreshed;
      } catch {
        return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
      }
    }
    const marker = this.options.ledger.markProviderAcknowledged;
    if (!marker) {
      return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
    }
    try {
      if (attempt.state === "Approved") {
        const eligible = this.options.ledger.canProviderAcknowledged;
        if (!eligible || !(await eligible.call(this.options.ledger, attempt))) {
          return result(
            attempt,
            false,
            true,
            "LINKLY_ACKNOWLEDGEMENT_PENDING",
          );
        }
      }
      await this.options.acknowledger.acknowledge(attempt);
      const acknowledgedAtIso = this.options.nowIso();
      const acknowledged = await marker.call(
        this.options.ledger,
        attempt,
        acknowledgedAtIso,
      );
      if (!acknowledged) {
        return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
      }
      return result(
        { ...attempt, providerAcknowledgedAtIso: acknowledgedAtIso },
        true,
        false,
        null,
      );
    } catch {
      return result(attempt, false, true, "LINKLY_ACKNOWLEDGEMENT_PENDING");
    }
  }
}

function isLinklyFinal(attempt: PaymentAttempt): boolean {
  return (
    attempt.provider === "linkly-cloud" &&
    (attempt.state === "Approved" ||
      attempt.state === "Declined" ||
      attempt.state === "Cancelled")
  );
}

function validEnvironment(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function result(
  attempt: PaymentAttempt,
  acknowledged: boolean,
  pending: boolean,
  errorCode: PaymentAcknowledgementErrorCode | null,
): PaymentAcknowledgementResult {
  return Object.freeze({ attempt, acknowledged, pending, errorCode });
}
