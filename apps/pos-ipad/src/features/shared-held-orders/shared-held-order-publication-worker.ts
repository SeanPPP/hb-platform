import {
  evaluateLegacyHeldOrderPayload,
  type SharedHeldOrderBlockReason,
} from "./legacy-held-order-evaluator";
import type { SharedPayloadEncryptorPort } from "./shared-held-order-claim-repository";
import type {
  SharedHeldOrderNetworkApiPort,
} from "./shared-held-order-network-api";
import type {
  SharedHeldOrderEvaluationRow,
  SharedHeldOrderPublicationQueuePort,
} from "./shared-held-order-publication-queue";
import { type SharedSaleCartV1 } from "./shared-sale-cart-v1";

import type { HeldOrderScope } from "@/core/contracts";

export type SharedHeldOrderPublicationRunResult = Readonly<{
  evaluatedOrders: number;
  stagedPendingPublish: number;
  blocked: number;
  published: number;
  failedCapability: number;
  failedPublish: number;
}>;

export type SharedHeldOrderPublicationWorkerOptions = Readonly<{
  queue: SharedHeldOrderPublicationQueuePort;
  api: SharedHeldOrderNetworkApiPort;
  encryptor: SharedPayloadEncryptorPort;
  nowIso(): string;
  scope: HeldOrderScope;
}>;

/**
 * 本地挂单后台 evaluator/publisher：NeedsEvaluation -> PendingPublish（密文原子落库）
 * -> Published（远端 revision/time 原子持久化）。服务端 disabled/网络不可用/版本不匹配
 * 只按退避记录失败，绝不删除或改变本地挂单。
 */
export class SharedHeldOrderPublicationWorker {
  public constructor(private readonly options: SharedHeldOrderPublicationWorkerOptions) {}

  public async runOnce(): Promise<SharedHeldOrderPublicationRunResult> {
    const nowIso = this.options.nowIso();
    let evaluatedOrders = 0;
    let stagedPendingPublish = 0;
    let blocked = 0;
    let published = 0;
    let failedCapability = 0;
    let failedPublish = 0;

    const needsEvaluation = (
      await this.options.queue.listNeedsEvaluation(this.options.scope, 200)
    ).filter((row) => isInPublicationScope(row, this.options.scope));
    for (const row of needsEvaluation) {
      const evaluation = await this.evaluate(row);
      const applied = await this.options.queue.applyShareEvaluation({
        holdId: row.holdId,
        evaluation,
        evaluatedAtIso: nowIso,
      });
      if (applied !== "updated") continue;
      evaluatedOrders += 1;
      if (evaluation.outcome === "pending-publish") {
        stagedPendingPublish += 1;
      } else {
        blocked += 1;
      }
    }

    const due = (
      await this.options.queue.listDue(this.options.scope, nowIso, 100)
    ).filter((row) => isInPublicationScope(row, this.options.scope));
    if (due.length === 0) {
      return freezeResult();
    }

    const capabilities = await this.readCapabilities();
    if (capabilities === "not-ready") {
      // 网络/服务不可用：每个 due 行记录稳定错误码形成退避，本地挂单保持 PendingPublish。
      for (const row of due) {
        await this.recordBackoff(
          row.holdId,
          nowIso,
          "SHARED_HELD_ORDER_CAPABILITY_UNAVAILABLE",
        );
      }
      failedCapability += due.length;
      return freezeResult();
    }
    if (capabilities === "disabled") {
      for (const row of due) {
        await this.recordBackoff(row.holdId, nowIso, "SHARED_HELD_ORDER_DISABLED");
      }
      failedCapability += due.length;
      return freezeResult();
    }
    if (capabilities === "version-mismatch") {
      for (const row of due) {
        await this.recordBackoff(
          row.holdId,
          nowIso,
          "SHARED_HELD_ORDER_VERSION_MISMATCH",
        );
      }
      failedCapability += due.length;
      return freezeResult();
    }

    for (const row of due) {
      try {
        const parsed = await this.parsePayload(row);
        const evaluation = evaluateLegacyHeldOrderPayload(parsed);
        if (evaluation.outcome !== "publishable") {
          await this.options.queue.blockPublication({
            holdId: row.holdId,
            reason: evaluation.reason,
            atIso: nowIso,
          });
          blocked += 1;
          continue;
        }
        const response = await this.options.api.publish({
          holdGuid: row.holdId,
          storeCode: row.storeCode,
          deviceCode: row.deviceCode,
          cart: evaluation.cart,
          idempotencyKey: idempotencyKeyFor(row.holdId),
        });
        if (response.holdGuid !== row.holdId) {
          // fail-closed：远端应答挂单与请求不一致，绝不 markPublished，只记稳定退避。
          await this.recordBackoff(row.holdId, nowIso, "SHARED_HELD_ORDER_MISMATCH");
          failedPublish += 1;
          continue;
        }
        const advanced = await this.options.queue.markPublished({
          holdId: row.holdId,
          remoteRevision: response.revision,
          remoteUpdatedAtIso: response.createdAtIso,
          expectedAttemptCount: row.publishAttemptCount,
          publishedAtIso: nowIso,
        });
        if (advanced) published += 1;
      } catch (error: unknown) {
        if (error instanceof PayloadParseFailure) {
          const didBlock = await this.options.queue.blockPublication({
            holdId: row.holdId,
            reason: blockedReasonForPayloadParse(error),
            atIso: nowIso,
          });
          if (didBlock) blocked += 1;
          continue;
        }
        const code =
          error instanceof Error && "code" in error
            ? String(error.code)
            : "SHARED_HELD_ORDER_PUBLISH_FAILED";
        await this.recordBackoff(row.holdId, nowIso, code);
        failedPublish += 1;
      }
    }
    return freezeResult();

    function freezeResult(): SharedHeldOrderPublicationRunResult {
      return Object.freeze({
        evaluatedOrders,
        stagedPendingPublish,
        blocked,
        published,
        failedCapability,
        failedPublish,
      });
    }
  }

  private async evaluate(
    row: SharedHeldOrderEvaluationRow,
  ): Promise<
    | Readonly<{ outcome: "pending-publish"; cart: SharedSaleCartV1 }>
    | Readonly<{ outcome: "blocked"; reason: SharedHeldOrderBlockReason }>
  > {
    try {
      const parsed = await this.parsePayload(row);
      const evaluation = evaluateLegacyHeldOrderPayload(parsed);
      if (evaluation.outcome === "publishable") {
        return { outcome: "pending-publish", cart: evaluation.cart };
      }
      return { outcome: "blocked", reason: evaluation.reason };
    } catch (error) {
      // 单行版本/密文/解密/JSON 解析失败只阻断该行，绝不让整轮中止。
      return {
        outcome: "blocked",
        reason: blockedReasonForPayloadParse(error),
      };
    }
  }

  private async parsePayload(
    row: Readonly<{
      payloadVersion: number;
      payloadCiphertext: Uint8Array;
    }>,
  ): Promise<unknown> {
    if (row.payloadVersion !== 1) {
      throw new PayloadParseFailure(
        "SHARED_HELD_ORDER_PAYLOAD_VERSION_UNSUPPORTED",
      );
    }
    if (!(row.payloadCiphertext instanceof Uint8Array)) {
      throw new PayloadParseFailure(
        "SHARED_HELD_ORDER_PAYLOAD_CIPHERTEXT_INVALID",
      );
    }
    let plaintext: string;
    try {
      plaintext = await this.options.encryptor.decrypt(row.payloadCiphertext);
    } catch {
      throw new PayloadParseFailure(
        "SHARED_HELD_ORDER_PAYLOAD_CIPHERTEXT_INVALID",
      );
    }
    try {
      return JSON.parse(plaintext) as unknown;
    } catch {
      throw new PayloadParseFailure("SHARED_HELD_ORDER_PAYLOAD_JSON_INVALID");
    }
  }

  private async readCapabilities(): Promise<
    "enabled" | "disabled" | "version-mismatch" | "not-ready"
  > {
    try {
      const capabilities = await this.options.api.getCapabilities();
      if (!capabilities.enabled) return "disabled";
      if (capabilities.payloadVersion !== 1) return "version-mismatch";
      return "enabled";
    } catch {
      return "not-ready";
    }
  }

  private async recordBackoff(
    holdId: string,
    atIso: string,
    errorCode: string,
  ): Promise<void> {
    await this.options.queue.recordPublishFailure({
      holdId,
      errorCode,
      failedAtIso: atIso,
    });
  }
}

function idempotencyKeyFor(holdGuid: string): string {
  return holdGuid;
}

/** 登录终端只能处理本门店、本设备创建的发布队列，避免跨 scope 误发。 */
function isInPublicationScope(
  row: Readonly<{ storeCode: string; deviceCode: string }>,
  scope: HeldOrderScope,
): boolean {
  return row.storeCode === scope.storeCode && row.deviceCode === scope.deviceCode;
}

/** payload 解析失败：只携带稳定机器码，绝不包含密文/明文/JSON 内容。 */
class PayloadParseFailure extends Error {
  public readonly code: string;

  public constructor(code: string) {
    super(code);
    this.name = "PayloadParseFailure";
    this.code = code;
  }
}

function blockedReasonForPayloadParse(
  error: unknown,
): SharedHeldOrderBlockReason {
  if (
    error instanceof PayloadParseFailure &&
    error.code === "SHARED_HELD_ORDER_PAYLOAD_VERSION_UNSUPPORTED"
  ) {
    return "LEGACY_PAYLOAD_VERSION_UNSUPPORTED";
  }
  return "LEGACY_PAYLOAD_CORRUPTED";
}
