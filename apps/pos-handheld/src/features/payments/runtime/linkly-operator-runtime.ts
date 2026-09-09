import {
  PAYMENT_PERMISSION,
  PaymentCheckoutRuntimeError,
  type PaymentPermissionGuard,
  type PaymentTrustedSessionGuard,
} from "./payment-checkout-runtime";
import type { LinklyRuntimeConfiguration } from "./payment-provider-registry";
import type { PaymentAcknowledgementRuntimePort } from "@hb/pos-payments-core/features/payments/payment-acknowledgement-service";

import type { PaymentAttempt } from "@/core/contracts";
import {
  LinklyCloudBackendApi,
  type LinklyCloudBackendSession,
} from "@/features/payments/linkly/linkly-cloud-backend";
import type { PaymentAttemptService } from "@hb/pos-payments-core/features/payments/payment-attempt-service";


export type LinklySafeOperatorKey =
  | "ok"
  | "cancel"
  | "yes"
  | "no"
  | "authorise";

export type LinklyOperatorStatus =
  | "in-progress"
  | "completed"
  | "declined"
  | "cancelled"
  | "recovery-required";

export type LinklyOperatorPublicResult = Readonly<{
  attemptId: string;
  status: LinklyOperatorStatus;
  errorCode: string | null;
  allowedKeys: readonly LinklySafeOperatorKey[];
  /** 已脱敏的终端显示快照，永远不携带 session、回单或通知原文。 */
  interaction?: LinklyOperatorInteraction | undefined;
}>;

export type LinklyOperatorInteraction = Readonly<{
  displayText: string | null;
  displayLines: readonly string[];
  inputType: string | null;
  graphicCode: string | null;
  recoveryAction: string | null;
}>;

export type LinklyOperatorReadInput = Readonly<{
  attemptId: string;
  signal?: AbortSignal;
  deadlineAtMs?: number;
}>;

export interface LinklyOperatorRuntimePort {
  /**
   * 公开命令只接受 attemptId 和枚举安全按键；sessionId 与任意 input data 均不越界。
   */
  sendKey(input: {
    attemptId: string;
    key: LinklySafeOperatorKey;
  }): Promise<LinklyOperatorPublicResult>;
  read(input: LinklyOperatorReadInput): Promise<LinklyOperatorPublicResult>;
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
    "status" | "sendKey" | "markReceiptPrinted"
  >;
  acknowledgements: PaymentAcknowledgementRuntimePort;
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
    const environment = requiredEnvironment(attempt);
    const current = await this.options.api.status(
      environment,
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
      environment,
      sessionId,
      linklyKey(input.key),
      null,
    );
    await this.assertAuthorized();
    assertSessionIdentity(updated, sessionId);
    return resultFromSession(attempt, updated, null);
  }

  public async read(input: LinklyOperatorReadInput): Promise<LinklyOperatorPublicResult> {
    const attempt = await this.requireAttempt(input.attemptId);
    if (input.signal?.aborted) {
      return resultFromAttempt(attempt, "recovery-required", "REQUEST_ABORTED");
    }
    const timeoutMs = readTimeoutMs(input.deadlineAtMs);
    if (timeoutMs === null) {
      return resultFromAttempt(
        attempt,
        "recovery-required",
        "LINKLY_RECOVERY_DEADLINE_EXCEEDED",
      );
    }
    const sessionId = requiredSessionId(attempt);
    const environment = attempt.providerEnvironment?.trim();
    if (!environment) {
      // 旧 attempt 必须先由 recovery/ACK 侧完成 legacy reconciliation；冷启动 UI 仍保留可恢复状态。
      return resultFromAttempt(
        attempt,
        "recovery-required",
        "LINKLY_ENVIRONMENT_RECONCILIATION_REQUIRED",
      );
    }
    const current = await this.options.api.status(
      environment,
      sessionId,
      input.signal,
      timeoutMs ?? undefined,
    );
    await this.assertAuthorized();
    assertSessionIdentity(current, sessionId);
    return resultFromSession(attempt, current, null);
  }

  public async markReceiptPrinted(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    const attempt = await this.requireAttempt(attemptId);
    const sessionId = requiredSessionId(attempt);
    const environment = requiredEnvironment(attempt);
    const updated = await this.options.api.markReceiptPrinted(
      environment,
      sessionId,
    );
    await this.assertAuthorized();
    assertSessionIdentity(updated, sessionId);
    return resultFromSession(attempt, updated, null);
  }

  public async acknowledge(
    attemptId: string,
  ): Promise<LinklyOperatorPublicResult> {
    await this.requireAttempt(attemptId);
    const updated = await this.options.acknowledgements.acknowledge(attemptId);
    await this.assertAuthorized();
    const status = updated.pending || !updated.acknowledged
      ? "recovery-required"
      : updated.attempt.state === "Cancelled"
        ? "cancelled"
        : updated.attempt.state === "Declined"
          ? "declined"
          : updated.attempt.state === "Approved"
            ? "completed"
            : "recovery-required";
    return resultFromAttempt(
      updated.attempt,
      status,
      updated.errorCode ??
        (status === "recovery-required"
          ? "LINKLY_ACKNOWLEDGEMENT_PENDING"
          : null),
    );
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

function requiredEnvironment(attempt: PaymentAttempt): string {
  const environment = attempt.providerEnvironment?.trim();
  if (!environment) {
    // 历史记录未冻结环境时，禁止拿当前配置猜测并向错误环境发送任何终端操作。
    throw new PaymentCheckoutRuntimeError("PAYMENT_ATTEMPT_IDENTITY_MISMATCH");
  }
  return environment;
}

function readTimeoutMs(deadlineAtMs: number | undefined): number | null | undefined {
  if (deadlineAtMs === undefined) return undefined;
  if (!Number.isFinite(deadlineAtMs)) return null;
  const remaining = Math.floor(deadlineAtMs - Date.now());
  return remaining > 0 ? Math.min(remaining, 240_000) : null;
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
  const signature = latestDisplayFlags(session);
  const keys: LinklySafeOperatorKey[] = [];
  // Linkly 的 OK/CANCEL 都发送 Key=0，但只有终端声明 Cancel 时才公开取消动作。
  if ((signature?.okKeyFlag ?? session.okKeyFlag)) keys.push("ok");
  if ((signature?.cancelKeyFlag ?? session.cancelKeyFlag)) keys.push("cancel");
  if ((signature?.acceptYesKeyFlag ?? session.acceptYesKeyFlag)) keys.push("yes");
  if ((signature?.declineNoKeyFlag ?? session.declineNoKeyFlag)) keys.push("no");
  if ((signature?.authoriseKeyFlag ?? session.authoriseKeyFlag)) keys.push("authorise");
  return keys;
}

function latestDisplayFlags(session: LinklyCloudBackendSession): Readonly<{
  okKeyFlag: boolean;
  cancelKeyFlag: boolean;
  acceptYesKeyFlag: boolean;
  declineNoKeyFlag: boolean;
  authoriseKeyFlag: boolean;
}> | null {
  const latest = session.notifications.filter((notification) =>
    notification.type.trim().toLowerCase() === "display").at(-1);
  if (!latest) return null;
  const disabled = { okKeyFlag: false, cancelKeyFlag: false, acceptYesKeyFlag: false, declineNoKeyFlag: false, authoriseKeyFlag: false };
  try {
    const response = nestedResponse(JSON.parse(latest.payloadJson) as unknown);
    if (!response) return disabled;
    // 最新终端 display 替代旧签名/顶层旗标；兼容 WPF 的 bool、整数和字符串编码。
    return {
      okKeyFlag: readFlag(response, "OKKeyFlag"),
      cancelKeyFlag: readFlag(response, "CancelKeyFlag"),
      acceptYesKeyFlag: readFlag(response, "AcceptYesKeyFlag"),
      declineNoKeyFlag: readFlag(response, "DeclineNoKeyFlag"),
      authoriseKeyFlag: readFlag(response, "AuthoriseKeyFlag"),
    };
  } catch {
    // 新通知损坏时失败关闭，不能回退并重新授权过期的签名或取消键。
    return disabled;
  }
}

function nestedResponse(value: unknown): Record<string, unknown> | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const root = value as Record<string, unknown>;
  const response = recordField(root, "Response");
  return response && typeof response === "object" && !Array.isArray(response)
    ? response as Record<string, unknown>
    : root;
}

function recordField(value: Record<string, unknown>, name: string): unknown {
  const fields = Object.entries(value).filter(([key]) => key.toLowerCase() === name.toLowerCase());
  return fields.length === 1 ? fields[0]![1] : undefined;
}

function readFlag(value: Record<string, unknown>, name: string): boolean {
  const flag = recordField(value, name);
  return flag === true ||
    (typeof flag === "number" && Number.isInteger(flag) && flag !== 0) ||
    (typeof flag === "string" && ["true", "1", "yes"].includes(flag.trim().toLowerCase()));
}

function linklyKey(key: LinklySafeOperatorKey): string {
  switch (key) {
    case "ok":
    case "cancel":
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
    interaction: interactionFromSession(session),
  };
}

function resultFromAttempt(
  attempt: PaymentAttempt,
  status: LinklyOperatorStatus,
  errorCode: string | null,
): LinklyOperatorPublicResult {
  return {
    attemptId: attempt.attemptId,
    status,
    errorCode,
    allowedKeys: Object.freeze([]),
    interaction: undefined,
  };
}

function interactionFromSession(
  session: LinklyCloudBackendSession,
): LinklyOperatorInteraction {
  return Object.freeze({
    displayText: boundedText(session.displayText),
    displayLines: Object.freeze(
      session.displayLines
        .map(boundedText)
        .filter((line): line is string => line !== null),
    ),
    inputType: boundedText(session.inputType),
    graphicCode: boundedText(session.graphicCode),
    recoveryAction: boundedText(session.recoveryAction),
  });
}

function boundedText(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized.slice(0, 160) : null;
}

function operatorStatus(
  session: LinklyCloudBackendSession,
): LinklyOperatorStatus {
  const normalized = session.status.trim().toLowerCase();
  if (normalized.includes("cancel")) return "cancelled";
  if (normalized.includes("declin")) return "declined";
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
