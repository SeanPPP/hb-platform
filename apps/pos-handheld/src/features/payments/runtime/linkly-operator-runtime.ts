import {
  PAYMENT_PERMISSION,
  PaymentCheckoutRuntimeError,
  type PaymentPermissionGuard,
  type PaymentTrustedSessionGuard,
} from "./payment-checkout-runtime";
import type { LinklyRuntimeConfiguration } from "./payment-provider-registry";

import type { PaymentAttempt } from "@/core/contracts";
import {
  LinklyCloudBackendApi,
  type LinklyCloudBackendSession,
} from "@/features/payments/linkly/linkly-cloud-backend";
import type { PaymentAttemptService } from "@/features/payments/payment-attempt-service";


export type LinklySafeOperatorKey =
  | "ok-cancel"
  | "yes"
  | "no"
  | "authorise";

export type LinklyOperatorStatus =
  | "in-progress"
  | "completed"
  | "cancelled"
  | "recovery-required";

export type LinklyOperatorPublicResult = Readonly<{
  attemptId: string;
  status: LinklyOperatorStatus;
  errorCode: string | null;
  allowedKeys: readonly LinklySafeOperatorKey[];
}>;

export interface LinklyOperatorRuntimePort {
  /**
   * 公开命令只接受 attemptId 和枚举安全按键；sessionId 与任意 input data 均不越界。
   */
  sendKey(input: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }): Promise<LinklyOperatorPublicResult>;
  markReceiptPrinted(attemptId: string): Promise<LinklyOperatorPublicResult>;
  acknowledge(attemptId: string): Promise<LinklyOperatorPublicResult>;
}

export type LinklyOperatorAttemptPort = Pick<
  PaymentAttemptService,
  "getAttempt"
>;

export type LinklyOperatorRuntimeOptions = Readonly<{
  attempts: LinklyOperatorAttemptPort;
  api: Pick<
    LinklyCloudBackendApi,
    "status" | "sendKey" | "markReceiptPrinted" | "acknowledge"
  >;
  configuration: LinklyRuntimeConfiguration;
  trustedSession: PaymentTrustedSessionGuard;
  permissions: PaymentPermissionGuard;
}>;

/**
 * Linkly 人工交互永远复用持久 attempt 上的既有 sessionId；这里没有 create 能力。
 */
export class LinklyOperatorRuntime implements LinklyOperatorRuntimePort {
  public constructor(private readonly options: LinklyOperatorRuntimeOptions) {}

  public async sendKey(input: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }): Promise<LinklyOperatorPublicResult> {
    const attempt = await this.requireAttempt(input.attemptId);
    if (attempt.state === "Unknown") {
      return resultFromAttempt(
        attempt,
        "recovery-required",
        "LINKLY_UNKNOWN_REQUIRES_RECOVERY",
      );
    }
    if (
      attempt.state !== "Submitted" &&
      attempt.state !== "Pending"
    ) {
      return resultFromAttempt(
        attempt,
        "recovery-required",
        "LINKLY_OPERATOR_STATE_INVALID",
      );
    }
    const sessionId = requiredSessionId(attempt);
    const current = await this.options.api.status(
      this.options.configuration.environment,
      sessionId,
    );
    await this.assertAuthorized();
    assertSessionIdentity(current, sessionId);
    if (!allowedKeys(current).includes(input.key)) {
      return resultFromSession(
        attempt,
        current,
        "LINKLY_OPERATOR_KEY_NOT_ALLOWED",
      );
    }

    const updated = await this.options.api.sendKey(
      this.options.configuration.environment,
      sessionId,
      linklyKey(input.key),
      null,
    );
    await this.assertAuthorized();
    assertSessionIdentity(updated, sessionId);
    return resultFromSession(attempt, updated, null);
  }

  public async markReceiptPrinted(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    const attempt = await this.requireAttempt(attemptId);
    const sessionId = requiredSessionId(attempt);
    const updated = await this.options.api.markReceiptPrinted(
      this.options.configuration.environment,
      sessionId,
    );
    await this.assertAuthorized();
    assertSessionIdentity(updated, sessionId);
    return resultFromSession(attempt, updated, null);
  }

  public async acknowledge(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    const attempt = await this.requireAttempt(attemptId);
    const sessionId = requiredSessionId(attempt);
    const updated = await this.options.api.acknowledge(
      this.options.configuration.environment,
      sessionId,
    );
    await this.assertAuthorized();
    assertSessionIdentity(updated, sessionId);
    return resultFromSession(attempt, updated, null);
  }

  private async requireAttempt(attemptId: string): Promise<PaymentAttempt> {
    await this.assertAuthorized();
    const normalizedAttemptId = attemptId.trim();
    if (!normalizedAttemptId) {
      throw new PaymentCheckoutRuntimeError("PAYMENT_ATTEMPT_NOT_FOUND");
    }
    const attempt = await this.options.attempts.getAttempt(normalizedAttemptId);
    await this.assertAuthorized();
    if (!attempt) {
      throw new PaymentCheckoutRuntimeError("PAYMENT_ATTEMPT_NOT_FOUND");
    }
    if (attempt.provider !== "linkly-cloud") {
      throw new PaymentCheckoutRuntimeError(
        "PAYMENT_ATTEMPT_IDENTITY_MISMATCH",
      );
    }
    return attempt;
  }

  private async assertAuthorized(): Promise<void> {
    await this.options.trustedSession.assertActive();
    await this.options.permissions.assert(PAYMENT_PERMISSION.view);
    await this.options.permissions.assert(PAYMENT_PERMISSION.takeCard);
    await this.options.permissions.assert(PAYMENT_PERMISSION.confirm);
    await this.options.trustedSession.assertActive();
  }
}

function requiredSessionId(attempt: PaymentAttempt): string {
  const sessionId = attempt.references.sessionId?.trim();
  if (!sessionId) {
    throw new PaymentCheckoutRuntimeError(
      "PAYMENT_ATTEMPT_IDENTITY_MISMATCH",
    );
  }
  return sessionId;
}

function assertSessionIdentity(
  session: LinklyCloudBackendSession,
  expectedSessionId: string,
): void {
  if (session.sessionId !== expectedSessionId) {
    throw new PaymentCheckoutRuntimeError(
      "PAYMENT_ATTEMPT_IDENTITY_MISMATCH",
    );
  }
}

function allowedKeys(
  session: LinklyCloudBackendSession,
): readonly LinklySafeOperatorKey[] {
  const keys: LinklySafeOperatorKey[] = [];
  if (session.okKeyFlag || session.cancelKeyFlag) keys.push("ok-cancel");
  if (session.acceptYesKeyFlag) keys.push("yes");
  if (session.declineNoKeyFlag) keys.push("no");
  if (session.authoriseKeyFlag) keys.push("authorise");
  return keys;
}

function linklyKey(key: LinklySafeOperatorKey): string {
  switch (key) {
    case "ok-cancel":
      return "0";
    case "yes":
      return "1";
    case "no":
      return "2";
    case "authorise":
      return "3";
  }
}

function resultFromSession(
  attempt: PaymentAttempt,
  session: LinklyCloudBackendSession,
  errorCode: string | null,
): LinklyOperatorPublicResult {
  return {
    attemptId: attempt.attemptId,
    status: operatorStatus(session),
    errorCode,
    allowedKeys: Object.freeze([...allowedKeys(session)]),
  };
}

function resultFromAttempt(
  attempt: PaymentAttempt,
  status: LinklyOperatorStatus,
  errorCode: string,
): LinklyOperatorPublicResult {
  return {
    attemptId: attempt.attemptId,
    status,
    errorCode,
    allowedKeys: Object.freeze([]),
  };
}

function operatorStatus(
  session: LinklyCloudBackendSession,
): LinklyOperatorStatus {
  const normalized = session.status.trim().toLowerCase();
  if (normalized.includes("cancel")) return "cancelled";
  if (
    normalized.includes("complete") &&
    session.transactionSuccess !== null
  ) {
    return "completed";
  }
  if (
    normalized.includes("progress") ||
    normalized.includes("process") ||
    normalized.includes("pending")
  ) {
    return "in-progress";
  }
  return "recovery-required";
}
