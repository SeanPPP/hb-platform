import type { SharedHeldOrderBlockReason } from "./legacy-held-order-evaluator";

import type { HeldOrderScope } from "@/core/contracts";
import type { SqliteConnectionPort } from "@/core/db/types";
import type { SharedHeldOrderShareRequestOutcome } from "@/features/held-orders/held-orders-domain";

export type { SharedHeldOrderShareRequestOutcome } from "@/features/held-orders/held-orders-domain";

export type SharedHeldOrderShareState =
  | "NeedsEvaluation"
  | "PendingPublish"
  | "Published"
  | "Blocked";

export type SharedHeldOrderPublicationBlockReason =
  | SharedHeldOrderBlockReason
  | "SHARED_HELD_ORDER_CANCELLED";

/** 待评估旧挂单：只暴露密文，不解密、不落明文。 */
export type SharedHeldOrderEvaluationRow = Readonly<{
  holdId: string;
  storeCode: string;
  deviceCode: string;
  payloadVersion: number;
  payloadCiphertext: Uint8Array;
}>;

export type SharedHeldOrderPublishDueRow = SharedHeldOrderEvaluationRow &
  Readonly<{
    publishAttemptCount: number;
    nextPublishAtIso: string | null;
    remoteRevision: number | null;
    remoteUpdatedAtIso: string | null;
  }>;

/** UI 只读共享状态：不暴露密文或 canonical payload。 */
export type SharedHeldOrderShareStateRow = Readonly<{
  holdId: string;
  shareState: SharedHeldOrderShareState;
  blockReason: string | null;
  requestedAtIso: string | null;
  isSyntheticSharedClaim: boolean;
}>;

export type ShareEvaluation =
  | Readonly<{ outcome: "pending-publish" }>
  | Readonly<{ outcome: "blocked"; reason: SharedHeldOrderBlockReason }>;

/**
 * 发布队列 Port：列 due、CAS 标记 Published、失败退避/错误码、稳定阻断。
 * 数据库只保留密文，队列绝不写入或返回明文 payload。
 */
export interface SharedHeldOrderPublicationQueuePort {
  requestShare(input: Readonly<{
    holdId: string;
    scope: HeldOrderScope;
    requestedAtIso: string;
  }>): Promise<SharedHeldOrderShareRequestOutcome>;
  listShareStates(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly SharedHeldOrderShareStateRow[]>;
  listNeedsEvaluation(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly SharedHeldOrderEvaluationRow[]>;
  applyShareEvaluation(input: Readonly<{
    holdId: string;
    evaluation: ShareEvaluation;
    evaluatedAtIso: string;
  }>): Promise<"updated" | "already-evaluated" | "not-found">;
  listDue(
    scope: HeldOrderScope,
    nowIso: string,
    limit: number,
  ): Promise<readonly SharedHeldOrderPublishDueRow[]>;
  markPublished(input: Readonly<{
    holdId: string;
    remoteRevision: number;
    remoteUpdatedAtIso: string;
    expectedAttemptCount: number;
    publishedAtIso: string;
  }>): Promise<boolean>;
  recordPublishFailure(input: Readonly<{
    holdId: string;
    errorCode: string;
    failedAtIso: string;
  }>): Promise<boolean>;
  blockPublication(input: Readonly<{
    holdId: string;
    reason: SharedHeldOrderPublicationBlockReason;
    atIso: string;
  }>): Promise<boolean>;
}

/** 发布失败退避：第 1 次失败后 30s，随后指数递增，封顶 1 小时。 */
export function publishRetryDelayMs(attemptCount: number): number {
  if (!Number.isSafeInteger(attemptCount) || attemptCount < 1) {
    throw new TypeError("publish attempt count must be a positive integer");
  }
  const baseMs = 30_000;
  const capMs = 3_600_000;
  const exponent = Math.min(attemptCount - 1, 20);
  return Math.min(baseMs * 2 ** exponent, capMs);
}

export class SqliteSharedHeldOrderPublicationQueue
  implements SharedHeldOrderPublicationQueuePort
{
  public constructor(private readonly db: SqliteConnectionPort) {}

  public async requestShare(input: Readonly<{
    holdId: string;
    scope: HeldOrderScope;
    requestedAtIso: string;
  }>): Promise<SharedHeldOrderShareRequestOutcome> {
    if (!input.requestedAtIso.trim()) {
      throw new TypeError("share requested at must not be blank");
    }
    // 共享意图和资格判断必须在同一写事务中完成，避免重复点击或换班时
    // 把别的门店/设备、synthetic claim 或非 Pending 行写成可发布事实。
    return this.db.withExclusiveTransaction(async (transaction) => {
      const row = await transaction.getFirst<{
        status: string;
        share_state: SharedHeldOrderShareState;
        share_requested_at_iso: string | null;
        is_synthetic_shared_claim: number;
      }>(
        `SELECT status, share_state, share_requested_at_iso, is_synthetic_shared_claim
         FROM held_order_records
         WHERE hold_id = ? AND store_code = ? AND device_code = ?`,
        [input.holdId, input.scope.storeCode, input.scope.deviceCode],
      );
      if (!row) return "not-found";
      if (
        row.status !== "Pending" ||
        row.is_synthetic_shared_claim !== 0
      ) {
        return "ineligible";
      }
      // 请求时间是一次性共享事实；worker 已推进 PendingPublish/Published/Blocked 后
      // 的重复调用仍返回 already-requested，且绝不再次发布或改写时间。
      if (row.share_requested_at_iso !== null) return "already-requested";
      if (row.share_state !== "NeedsEvaluation") return "ineligible";

      const changed = await transaction.run(
        `UPDATE held_order_records
         SET share_requested_at_iso = ?, updated_at_iso = ?
         WHERE hold_id = ? AND store_code = ? AND device_code = ?
           AND status = 'Pending'
           AND share_state = 'NeedsEvaluation'
           AND is_synthetic_shared_claim = 0
           AND share_requested_at_iso IS NULL`,
        [
          input.requestedAtIso,
          input.requestedAtIso,
          input.holdId,
          input.scope.storeCode,
          input.scope.deviceCode,
        ],
      );
      return changed.changes === 1 ? "requested" : "ineligible";
    });
  }

  public async listShareStates(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly SharedHeldOrderShareStateRow[]> {
    const rows = await this.db.getAll<{
      hold_id: string;
      share_state: SharedHeldOrderShareState;
      publish_block_reason: string | null;
      share_requested_at_iso: string | null;
      is_synthetic_shared_claim: number;
    }>(
      `SELECT hold_id, share_state, publish_block_reason,
              share_requested_at_iso, is_synthetic_shared_claim
       FROM held_order_records
       WHERE store_code = ?
         AND device_code = ?
         AND status IN ('Pending', 'Recalling')
       ORDER BY local_sequence ASC
       LIMIT ?`,
      [scope.storeCode, scope.deviceCode, limit],
    );
    return rows.map((row) => ({
      holdId: row.hold_id,
      shareState: row.share_state,
      blockReason: row.publish_block_reason,
      requestedAtIso: row.share_requested_at_iso,
      isSyntheticSharedClaim: row.is_synthetic_shared_claim === 1,
    }));
  }

  public async listNeedsEvaluation(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly SharedHeldOrderEvaluationRow[]> {
    const rows = await this.db.getAll<{
      hold_id: string;
      store_code: string;
      device_code: string;
      payload_version: number;
      payload_ciphertext: Uint8Array;
    }>(
      `SELECT hold_id, store_code, device_code, payload_version, payload_ciphertext
       FROM held_order_records
       WHERE share_state = 'NeedsEvaluation'
         AND share_requested_at_iso IS NOT NULL
         AND is_synthetic_shared_claim = 0
         AND store_code = ?
         AND device_code = ?
         AND status NOT IN ('Recalling', 'Recalled')
       ORDER BY local_sequence ASC
       LIMIT ?`,
      [scope.storeCode, scope.deviceCode, limit],
    );
    return rows.map((row) => ({
      holdId: row.hold_id,
      storeCode: row.store_code,
      deviceCode: row.device_code,
      payloadVersion: row.payload_version,
      payloadCiphertext: row.payload_ciphertext,
    }));
  }

  public async applyShareEvaluation(input: Readonly<{
    holdId: string;
    evaluation: ShareEvaluation;
    evaluatedAtIso: string;
  }>): Promise<"updated" | "already-evaluated" | "not-found"> {
    const updated =
      input.evaluation.outcome === "pending-publish"
        ? await this.db.run(
            `UPDATE held_order_records
             SET share_state = 'PendingPublish',
                 next_publish_at_iso = ?,
                 publish_error_code = NULL,
                 publish_block_reason = NULL,
                 updated_at_iso = ?
             WHERE hold_id = ? AND share_state = 'NeedsEvaluation'
               AND share_requested_at_iso IS NOT NULL
               AND is_synthetic_shared_claim = 0`,
            [input.evaluatedAtIso, input.evaluatedAtIso, input.holdId],
          )
        : await this.db.run(
            `UPDATE held_order_records
             SET share_state = 'Blocked',
                 next_publish_at_iso = NULL,
                 publish_error_code = NULL,
                 publish_block_reason = ?,
                 updated_at_iso = ?
             WHERE hold_id = ? AND share_state = 'NeedsEvaluation'
               AND share_requested_at_iso IS NOT NULL
               AND is_synthetic_shared_claim = 0`,
            [
              input.evaluation.reason,
              input.evaluatedAtIso,
              input.holdId,
            ],
          );
    if (updated.changes === 1) return "updated";
    const exists = await this.db.getFirst<{ hold_id: string }>(
      "SELECT hold_id FROM held_order_records WHERE hold_id = ?",
      [input.holdId],
    );
    return exists ? "already-evaluated" : "not-found";
  }

  public async listDue(
    scope: HeldOrderScope,
    nowIso: string,
    limit: number,
  ): Promise<readonly SharedHeldOrderPublishDueRow[]> {
    const rows = await this.db.getAll<{
      hold_id: string;
      store_code: string;
      device_code: string;
      payload_version: number;
      payload_ciphertext: Uint8Array;
      publish_attempt_count: number;
      next_publish_at_iso: string | null;
      remote_revision: number | null;
      remote_updated_at_iso: string | null;
    }>(
      `SELECT hold_id, store_code, device_code, payload_version, payload_ciphertext,
              publish_attempt_count, next_publish_at_iso, remote_revision, remote_updated_at_iso
       FROM held_order_records
       WHERE share_state = 'PendingPublish'
         AND share_requested_at_iso IS NOT NULL
         AND is_synthetic_shared_claim = 0
         AND store_code = ?
         AND device_code = ?
         AND status NOT IN ('Recalling', 'Recalled')
         AND (next_publish_at_iso IS NULL OR next_publish_at_iso <= ?)
       ORDER BY next_publish_at_iso ASC, updated_at_iso ASC
       LIMIT ?`,
      [scope.storeCode, scope.deviceCode, nowIso, limit],
    );
    return rows.map((row) => ({
      holdId: row.hold_id,
      storeCode: row.store_code,
      deviceCode: row.device_code,
      payloadVersion: row.payload_version,
      payloadCiphertext: row.payload_ciphertext,
      publishAttemptCount: row.publish_attempt_count,
      nextPublishAtIso: row.next_publish_at_iso,
      remoteRevision: row.remote_revision,
      remoteUpdatedAtIso: row.remote_updated_at_iso,
    }));
  }

  public async markPublished(input: Readonly<{
    holdId: string;
    remoteRevision: number;
    remoteUpdatedAtIso: string;
    expectedAttemptCount: number;
    publishedAtIso: string;
  }>): Promise<boolean> {
    const result = await this.db.run(
      `UPDATE held_order_records
       SET share_state = 'Published',
           remote_revision = ?,
           remote_updated_at_iso = ?,
           next_publish_at_iso = NULL,
           publish_error_code = NULL,
           publish_block_reason = NULL,
           updated_at_iso = ?
       WHERE hold_id = ?
         AND share_state = 'PendingPublish'
         AND share_requested_at_iso IS NOT NULL
         AND is_synthetic_shared_claim = 0
         AND status = 'Pending'
         AND publish_attempt_count = ?`,
      [
        input.remoteRevision,
        input.remoteUpdatedAtIso,
        input.publishedAtIso,
        input.holdId,
        input.expectedAttemptCount,
      ],
    );
    return result.changes === 1;
  }

  public async recordPublishFailure(input: Readonly<{
    holdId: string;
    errorCode: string;
    failedAtIso: string;
  }>): Promise<boolean> {
    return this.db.withExclusiveTransaction(async (transaction) => {
      const row = await transaction.getFirst<{
        publish_attempt_count: number;
      }>(
        `SELECT publish_attempt_count
         FROM held_order_records
         WHERE hold_id = ? AND share_state = 'PendingPublish'
           AND share_requested_at_iso IS NOT NULL
           AND is_synthetic_shared_claim = 0`,
        [input.holdId],
      );
      if (!row) return false;
      const nextAttempt = row.publish_attempt_count + 1;
      const nextPublishAtIso = addIsoMilliseconds(
        input.failedAtIso,
        publishRetryDelayMs(nextAttempt),
      );
      const result = await transaction.run(
        `UPDATE held_order_records
         SET publish_attempt_count = ?,
             next_publish_at_iso = ?,
             publish_error_code = ?,
             updated_at_iso = ?
         WHERE hold_id = ?
           AND share_state = 'PendingPublish'
           AND share_requested_at_iso IS NOT NULL
           AND is_synthetic_shared_claim = 0
           AND publish_attempt_count = ?`,
        [
          nextAttempt,
          nextPublishAtIso,
          input.errorCode,
          input.failedAtIso,
          input.holdId,
          row.publish_attempt_count,
        ],
      );
      return result.changes === 1;
    });
  }

  public async blockPublication(input: Readonly<{
    holdId: string;
    reason: SharedHeldOrderPublicationBlockReason;
    atIso: string;
  }>): Promise<boolean> {
    const result = await this.db.run(
      `UPDATE held_order_records
       SET share_state = 'Blocked',
           publish_block_reason = ?,
           next_publish_at_iso = NULL,
           publish_error_code = NULL,
           updated_at_iso = ?
       WHERE hold_id = ?
         AND share_state IN ('NeedsEvaluation', 'PendingPublish')
         AND share_requested_at_iso IS NOT NULL
         AND is_synthetic_shared_claim = 0`,
      [input.reason, input.atIso, input.holdId],
    );
    return result.changes === 1;
  }
}

function addIsoMilliseconds(iso: string, milliseconds: number): string {
  const parsed = Date.parse(iso);
  if (!Number.isFinite(parsed)) {
    throw new TypeError("failedAtIso must be a valid ISO timestamp");
  }
  return new Date(parsed + milliseconds).toISOString();
}
