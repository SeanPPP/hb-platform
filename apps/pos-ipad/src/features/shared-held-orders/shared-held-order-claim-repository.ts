import {
  normalizeSharedSaleCartV1,
  type SharedSaleCartV1,
} from "./shared-sale-cart-v1";
import { fromSharedSaleCartV1 } from "./shared-held-order-cart-reverse-mapper";

import type { HeldOrderScope } from "@/core/contracts";
import { multiplyCentsAwayFromZero } from "@/core/contracts/money";
import type { SqliteConnectionPort } from "@/core/db/types";


/**
 * 与现有 SensitivePayloadEncryptor（sqlite-repositories）结构一致的适配口；
 * 组合层后续可直接注入真实实现，本层只存密文。
 */
export interface SharedPayloadEncryptorPort {
  encrypt(plaintext: string): Promise<Uint8Array>;
  decrypt(ciphertext: Uint8Array): Promise<string>;
}

export type SharedHeldOrderClaimSource = "RemoteClaim" | "OfflineOrigin";
export type SharedHeldOrderClaimState =
  | "Prepared"
  | "Active"
  | "Completed"
  | "Released"
  | "Superseded";

export type SharedHeldOrderClaim = Readonly<{
  claimGuid: string;
  holdGuid: string;
  recallAttemptId: string;
  scope: HeldOrderScope;
  source: SharedHeldOrderClaimSource;
  state: SharedHeldOrderClaimState;
  prepareIdempotencyKey: string;
  activateIdempotencyKey: string | null;
  releaseIdempotencyKey: string | null;
  supersedeIdempotencyKey: string | null;
  payload: SharedSaleCartV1;
  serverRevision: number | null;
  preparedExpiresAtIso: string;
  heldAtIso: string;
  heldBy: Readonly<{ cashierId: string; cashierName: string }>;
  boundOrderGuid: string | null;
  createdAtIso: string;
  updatedAtIso: string;
}>;

/** prepare 的严格本机输入：payload 必须通过 SharedSaleCartV1 校验，绝不落明文。 */
export type PreparedClaimInput = Readonly<{
  claimGuid: string;
  holdGuid: string;
  recallAttemptId: string;
  scope: HeldOrderScope;
  source: SharedHeldOrderClaimSource;
  prepareIdempotencyKey: string;
  payload: SharedSaleCartV1;
  preparedExpiresAtIso: string;
  heldAtIso: string;
  heldBy: Readonly<{ cashierId: string; cashierName: string }>;
  createdAtIso: string;
}>;

export type PrepareClaimResult =
  | Readonly<{ outcome: "prepared"; claim: SharedHeldOrderClaim }>
  | Readonly<{ outcome: "replayed"; claim: SharedHeldOrderClaim }>
  | Readonly<{ outcome: "fence-held"; winner: SharedHeldOrderClaim }>;

export class SharedHeldOrderClaimInvariantError extends Error {
  public readonly code = "MULTIPLE_OPEN_CLAIMS" as const;

  public constructor() {
    super("同一终端存在多个未完成的共享挂单 claim。");
    this.name = "SharedHeldOrderClaimInvariantError";
  }
}

/**
 * 持久化取单声明 Port：prepare 在同一 exclusive transaction 写入 claim 密文、
 * RecallActive fence 与（远端必需的）synthetic Recalling held 行；激活/绑定/完成/
 * 释放/替换均带幂等键；恢复只列声明，绝不自动释放 Active。
 */
export interface SharedHeldOrderClaimRepositoryPort {
  prepareClaim(input: PreparedClaimInput): Promise<PrepareClaimResult>;
  activatePreparedClaim(input: Readonly<{
    claimGuid: string;
    prepareIdempotencyKey: string;
    activateIdempotencyKey: string;
    serverRevision: number | null;
    activatedAtIso: string;
  }>): Promise<boolean>;
  bindOrderToActiveClaim(input: Readonly<{
    claimGuid: string;
    activateIdempotencyKey: string;
    boundOrderGuid: string;
    boundAtIso: string;
  }>): Promise<boolean>;
  completeActiveClaim(input: Readonly<{
    claimGuid: string;
    activateIdempotencyKey: string;
    releaseIdempotencyKey: string;
    completedAtIso: string;
  }>): Promise<boolean>;
  releaseClaim(input: Readonly<{
    claimGuid: string;
    releaseIdempotencyKey: string;
    releasedAtIso: string;
    expectedState: "Prepared" | "Active";
  }>): Promise<boolean>;
  /**
   * 兼容旧版 release 已先清除 legacy fence/held、却遗漏 shared claim 的状态。
   * 实现只能修复精确匹配的 OfflineOrigin Active 孤儿，正常 open claim 必须拒绝。
   */
  repairLegacyClearedOfflineOriginClaim(input: Readonly<{
    claimGuid: string;
    releaseIdempotencyKey: string;
    releasedAtIso: string;
  }>): Promise<boolean>;
  /**
   * 支付草稿已恢复相同 binding 时，兼容旧版 release 留下的 Active 孤儿：
   * 仅在 held 已回到 Pending 且 fence 缺失时原子补回 Recalling/fence。
   */
  ensureRestoredOfflineOriginClaimFence(input: Readonly<{
    claimGuid: string;
    repairedAtIso: string;
  }>): Promise<boolean>;
  supersedeClaim(input: Readonly<{
    claimGuid: string;
    supersedeIdempotencyKey: string;
    supersededAtIso: string;
    expectedState: "Prepared" | "Active";
  }>): Promise<boolean>;
  getClaim(claimGuid: string): Promise<SharedHeldOrderClaim | null>;
  /** scope 内 Prepared/Active 受唯一索引保护；该权威查询不受历史分页影响。 */
  getOpenClaim(scope: HeldOrderScope): Promise<SharedHeldOrderClaim | null>;
  /** 对账专用：列出全部 open claim，以便收敛旧版或损坏态的重复事实。 */
  listOpenClaims(scope: HeldOrderScope): Promise<readonly SharedHeldOrderClaim[]>;
  /** 精确查询某挂单最新 claim，供 owner release 区分“不存在”与终态冲突。 */
  getLatestClaimForHold(
    scope: HeldOrderScope,
    holdGuid: string,
  ): Promise<SharedHeldOrderClaim | null>;
  listMine(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly SharedHeldOrderClaim[]>;
}

type ClaimRow = {
  claim_guid: string;
  hold_guid: string;
  recall_attempt_id: string;
  store_code: string;
  device_code: string;
  source: SharedHeldOrderClaimSource;
  state: SharedHeldOrderClaimState;
  prepare_idempotency_key: string;
  activate_idempotency_key: string | null;
  release_idempotency_key: string | null;
  supersede_idempotency_key: string | null;
  payload_version: number;
  payload_ciphertext: Uint8Array;
  server_revision: number | null;
  prepared_expires_at_iso: string;
  held_at_iso: string;
  held_by_cashier_id: string;
  held_by_cashier_name: string;
  bound_order_guid: string | null;
  created_at_iso: string;
  updated_at_iso: string;
};

export class SqliteSharedHeldOrderClaimRepository
  implements SharedHeldOrderClaimRepositoryPort
{
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly encryptor: SharedPayloadEncryptorPort,
  ) {}

  public async prepareClaim(
    input: PreparedClaimInput,
  ): Promise<PrepareClaimResult> {
    const facts = validatePreparedClaimInput(input);
    const ciphertext = await this.encryptor.encrypt(
      JSON.stringify(facts.payload),
    );
    if (!(ciphertext instanceof Uint8Array) || ciphertext.length === 0) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_PAYLOAD_ENCRYPTION_FAILED");
    }
    const localHeldCiphertext = facts.source === "RemoteClaim"
      ? await this.encryptor.encrypt(JSON.stringify({
          version: 1,
          pricingState: fromSharedSaleCartV1(facts.payload),
        }))
      : null;
    if (
      localHeldCiphertext !== null &&
      (!(localHeldCiphertext instanceof Uint8Array) || localHeldCiphertext.length === 0)
    ) {
      throw new Error("SHARED_HELD_ORDER_LOCAL_PAYLOAD_ENCRYPTION_FAILED");
    }
    return this.db.withExclusiveTransaction(async (transaction) => {
      // 预检与写入同事务，崩溃后同 key 重放不重复建行、不同 facts 拒绝。
      const existing = await this.loadByPrepareKey(
        transaction,
        facts.prepareIdempotencyKey,
      );
      if (existing) {
        if (!samePrepareFacts(existing, facts)) {
          throw new Error("SHARED_HELD_ORDER_CLAIM_PREPARE_FACTS_MISMATCH");
        }
        return { outcome: "replayed", claim: existing };
      }
      const winner = await this.loadScopeFenceWinner(transaction, facts.scope);
      if (winner) return { outcome: "fence-held", winner };
      await this.assertTerminalFenceAvailable(transaction, facts);
      await this.prepareLocalHeldRow(transaction, facts, localHeldCiphertext);
      await transaction.run(
        `INSERT INTO shared_held_order_claim_records (
          claim_guid, hold_guid, recall_attempt_id, store_code, device_code,
          source, state, prepare_idempotency_key, payload_version,
          payload_ciphertext, prepared_expires_at_iso, held_at_iso,
          held_by_cashier_id, held_by_cashier_name, created_at_iso, updated_at_iso
        ) VALUES (?, ?, ?, ?, ?, ?, 'Prepared', ?, 1, ?, ?, ?, ?, ?, ?, ?)`,
        [
          facts.claimGuid,
          facts.holdGuid,
          facts.recallAttemptId,
          facts.scope.storeCode,
          facts.scope.deviceCode,
          facts.source,
          facts.prepareIdempotencyKey,
          ciphertext,
          facts.preparedExpiresAtIso,
          facts.heldAtIso,
          facts.heldBy.cashierId,
          facts.heldBy.cashierName,
          facts.createdAtIso,
          facts.createdAtIso,
        ],
      );
      await transaction.run(
        `INSERT INTO terminal_cart_fences (
          store_code, device_code, kind, hold_id, recall_attempt_id,
          bound_order_guid, created_at_iso
        ) VALUES (?, ?, 'RecallActive', ?, ?, NULL, ?)`,
        [
          facts.scope.storeCode,
          facts.scope.deviceCode,
          facts.holdGuid,
          facts.recallAttemptId,
          facts.createdAtIso,
        ],
      );
      const claim = await this.loadByGuid(transaction, facts.claimGuid);
      if (!claim) {
        throw new Error("SHARED_HELD_ORDER_CLAIM_INSERT_MISSING");
      }
      return { outcome: "prepared", claim };
    });
  }

  public async activatePreparedClaim(input: Readonly<{
    claimGuid: string;
    prepareIdempotencyKey: string;
    activateIdempotencyKey: string;
    serverRevision: number | null;
    activatedAtIso: string;
  }>): Promise<boolean> {
    const result = await this.db.run(
      `UPDATE shared_held_order_claim_records
       SET state = 'Active',
           activate_idempotency_key = ?,
           server_revision = ?,
           updated_at_iso = ?
       WHERE claim_guid = ?
         AND state = 'Prepared'
         AND prepare_idempotency_key = ?
         AND activate_idempotency_key IS NULL`,
      [
        input.activateIdempotencyKey,
        input.serverRevision,
        input.activatedAtIso,
        input.claimGuid,
        input.prepareIdempotencyKey,
      ],
    );
    if (result.changes === 1) return true;
    const claim = await this.loadByGuid(this.db, input.claimGuid);
    return (
      claim?.state === "Active" &&
      claim.activateIdempotencyKey === input.activateIdempotencyKey
    );
  }

  public async bindOrderToActiveClaim(input: Readonly<{
    claimGuid: string;
    activateIdempotencyKey: string;
    boundOrderGuid: string;
    boundAtIso: string;
  }>): Promise<boolean> {
    const result = await this.db.run(
      `UPDATE shared_held_order_claim_records
       SET bound_order_guid = ?,
           updated_at_iso = ?
       WHERE claim_guid = ?
         AND state = 'Active'
         AND activate_idempotency_key = ?
         AND bound_order_guid IS NULL`,
      [
        input.boundOrderGuid,
        input.boundAtIso,
        input.claimGuid,
        input.activateIdempotencyKey,
      ],
    );
    if (result.changes === 1) return true;
    const claim = await this.loadByGuid(this.db, input.claimGuid);
    return (
      claim?.state === "Active" &&
      claim.activateIdempotencyKey === input.activateIdempotencyKey &&
      claim.boundOrderGuid === input.boundOrderGuid
    );
  }

  public async completeActiveClaim(input: Readonly<{
    claimGuid: string;
    activateIdempotencyKey: string;
    releaseIdempotencyKey: string;
    completedAtIso: string;
  }>): Promise<boolean> {
    return this.db.withExclusiveTransaction(async (transaction) => {
      const claim = await this.loadByGuid(transaction, input.claimGuid);
      if (!claim) return false;
      if (
        claim.state === "Completed" &&
        claim.releaseIdempotencyKey === input.releaseIdempotencyKey
      ) {
        return true;
      }
      if (
        claim.state !== "Active" ||
        claim.activateIdempotencyKey !== input.activateIdempotencyKey ||
        claim.releaseIdempotencyKey !== null ||
        claim.boundOrderGuid === null
      ) {
        return false;
      }
      await this.finishRecallHeldAndFence(transaction, claim, input.completedAtIso);
      const updated = await transaction.run(
        `UPDATE shared_held_order_claim_records
         SET state = 'Completed',
             release_idempotency_key = ?,
             updated_at_iso = ?
         WHERE claim_guid = ?
           AND state = 'Active'
           AND activate_idempotency_key = ?
           AND bound_order_guid IS NOT NULL
           AND release_idempotency_key IS NULL`,
        [
          input.releaseIdempotencyKey,
          input.completedAtIso,
          input.claimGuid,
          input.activateIdempotencyKey,
        ],
      );
      if (updated.changes !== 1) {
        throw new Error("SHARED_HELD_ORDER_CLAIM_COMPLETE_CHANGED");
      }
      return true;
    });
  }

  public async releaseClaim(input: Readonly<{
    claimGuid: string;
    releaseIdempotencyKey: string;
    releasedAtIso: string;
    expectedState: "Prepared" | "Active";
  }>): Promise<boolean> {
    return this.db.withExclusiveTransaction(async (transaction) => {
      const claim = await this.loadByGuid(transaction, input.claimGuid);
      if (!claim) return false;
      if (
        claim.state === "Released" &&
        claim.releaseIdempotencyKey === input.releaseIdempotencyKey
      ) {
        return true;
      }
      if (
        claim.state !== input.expectedState ||
        claim.releaseIdempotencyKey !== null ||
        claim.boundOrderGuid !== null
      ) {
        return false;
      }
      // 先清本地 synthetic held/fence，再 CAS 终态；任一失败整体回滚。
      await this.clearClaimFenceAndHeld(transaction, claim, input.releasedAtIso);
      const updated = await transaction.run(
        `UPDATE shared_held_order_claim_records
         SET state = 'Released',
             release_idempotency_key = ?,
             updated_at_iso = ?
         WHERE claim_guid = ?
           AND state = ?
           AND release_idempotency_key IS NULL
           AND bound_order_guid IS NULL`,
        [
          input.releaseIdempotencyKey,
          input.releasedAtIso,
          input.claimGuid,
          input.expectedState,
        ],
      );
      if (updated.changes !== 1) {
        throw new Error("SHARED_HELD_ORDER_CLAIM_RELEASE_CHANGED");
      }
      return true;
    });
  }

  public async repairLegacyClearedOfflineOriginClaim(input: Readonly<{
    claimGuid: string;
    releaseIdempotencyKey: string;
    releasedAtIso: string;
  }>): Promise<boolean> {
    return this.db.withExclusiveTransaction(async (transaction) => {
      const claim = await this.loadByGuid(transaction, input.claimGuid);
      if (!claim) return false;
      if (
        claim.state === "Released" &&
        claim.releaseIdempotencyKey === input.releaseIdempotencyKey
      ) {
        return true;
      }
      if (
        claim.source !== "OfflineOrigin" ||
        claim.state !== "Active" ||
        claim.activateIdempotencyKey === null ||
        claim.serverRevision !== null ||
        claim.releaseIdempotencyKey !== null ||
        claim.boundOrderGuid !== null
      ) {
        return false;
      }

      const fence = await transaction.getFirst<{ present: number }>(
        `SELECT 1 AS present FROM terminal_cart_fences
         WHERE store_code = ? AND device_code = ?
           AND kind = 'RecallActive' AND hold_id = ? AND recall_attempt_id = ?`,
        [
          claim.scope.storeCode,
          claim.scope.deviceCode,
          claim.holdGuid,
          claim.recallAttemptId,
        ],
      );
      if (fence) return false;

      const held = await transaction.getFirst<{
        status: string;
        recall_attempt_id: string | null;
        recalling_at_iso: string | null;
        recalling_cashier_id: string | null;
        recalling_cashier_name: string | null;
        is_synthetic_shared_claim: number;
      }>(
        `SELECT status, recall_attempt_id, recalling_at_iso,
                recalling_cashier_id, recalling_cashier_name,
                is_synthetic_shared_claim
         FROM held_order_records
         WHERE hold_id = ? AND store_code = ? AND device_code = ?`,
        [claim.holdGuid, claim.scope.storeCode, claim.scope.deviceCode],
      );
      if (
        !held ||
        held.status !== "Pending" ||
        held.recall_attempt_id !== null ||
        held.recalling_at_iso !== null ||
        held.recalling_cashier_id !== null ||
        held.recalling_cashier_name !== null ||
        held.is_synthetic_shared_claim !== 0
      ) {
        return false;
      }

      // 只有旧 release 的完整后置事实成立时才补写 shared claim 终态；不清理
      // 任何其他 fence/held，也不放宽普通 releaseClaim 的 fail-closed 语义。
      const updated = await transaction.run(
        `UPDATE shared_held_order_claim_records
         SET state = 'Released', release_idempotency_key = ?, updated_at_iso = ?
         WHERE claim_guid = ?
           AND source = 'OfflineOrigin'
           AND state = 'Active'
           AND activate_idempotency_key IS NOT NULL
           AND server_revision IS NULL
           AND release_idempotency_key IS NULL
           AND bound_order_guid IS NULL`,
        [
          input.releaseIdempotencyKey,
          input.releasedAtIso,
          input.claimGuid,
        ],
      );
      if (updated.changes !== 1) {
        throw new Error("SHARED_HELD_ORDER_LEGACY_ORPHAN_REPAIR_CHANGED");
      }
      return true;
    });
  }

  public async ensureRestoredOfflineOriginClaimFence(input: Readonly<{
    claimGuid: string;
    repairedAtIso: string;
  }>): Promise<boolean> {
    const claimGuid = nonBlank(input.claimGuid, "claim guid");
    const repairedAtIso = canonicalIso(input.repairedAtIso, "repaired at");
    return this.db.withExclusiveTransaction(async (transaction) => {
      const claim = await this.loadByGuid(transaction, claimGuid);
      if (
        !claim ||
        claim.source !== "OfflineOrigin" ||
        claim.state !== "Active" ||
        claim.activateIdempotencyKey === null ||
        claim.serverRevision !== null ||
        claim.releaseIdempotencyKey !== null ||
        claim.supersedeIdempotencyKey !== null ||
        claim.boundOrderGuid !== null
      ) {
        return false;
      }

      const fence = await transaction.getFirst<{
        kind: string;
        hold_id: string;
        recall_attempt_id: string | null;
        bound_order_guid: string | null;
      }>(
        `SELECT kind, hold_id, recall_attempt_id, bound_order_guid
         FROM terminal_cart_fences
         WHERE store_code = ? AND device_code = ?`,
        [claim.scope.storeCode, claim.scope.deviceCode],
      );
      const held = await transaction.getFirst<{
        status: string;
        recall_attempt_id: string | null;
        recalling_at_iso: string | null;
        recalling_cashier_id: string | null;
        recalling_cashier_name: string | null;
        is_synthetic_shared_claim: number;
      }>(
        `SELECT status, recall_attempt_id, recalling_at_iso,
                recalling_cashier_id, recalling_cashier_name,
                is_synthetic_shared_claim
         FROM held_order_records
         WHERE hold_id = ? AND store_code = ? AND device_code = ?`,
        [claim.holdGuid, claim.scope.storeCode, claim.scope.deviceCode],
      );

      const healthy =
        fence?.kind === "RecallActive" &&
        fence.hold_id === claim.holdGuid &&
        fence.recall_attempt_id === claim.recallAttemptId &&
        fence.bound_order_guid === null &&
        held?.status === "Recalling" &&
        held.recall_attempt_id === claim.recallAttemptId &&
        held.recalling_at_iso === claim.createdAtIso &&
        held.recalling_cashier_id === claim.heldBy.cashierId &&
        held.recalling_cashier_name === claim.heldBy.cashierName &&
        held.is_synthetic_shared_claim === 0;
      if (healthy) return true;

      if (
        fence !== null ||
        !held ||
        held.status !== "Pending" ||
        held.recall_attempt_id !== null ||
        held.recalling_at_iso !== null ||
        held.recalling_cashier_id !== null ||
        held.recalling_cashier_name !== null ||
        held.is_synthetic_shared_claim !== 0
      ) {
        return false;
      }

      const changed = await transaction.run(
        `UPDATE held_order_records
         SET status = 'Recalling', recalling_at_iso = ?, recall_attempt_id = ?,
             recalling_cashier_id = ?, recalling_cashier_name = ?,
             updated_at_iso = ?
         WHERE hold_id = ? AND store_code = ? AND device_code = ?
           AND status = 'Pending' AND recall_attempt_id IS NULL
           AND recalling_at_iso IS NULL AND recalling_cashier_id IS NULL
           AND recalling_cashier_name IS NULL AND is_synthetic_shared_claim = 0`,
        [
          claim.createdAtIso,
          claim.recallAttemptId,
          claim.heldBy.cashierId,
          claim.heldBy.cashierName,
          repairedAtIso,
          claim.holdGuid,
          claim.scope.storeCode,
          claim.scope.deviceCode,
        ],
      );
      if (changed.changes !== 1) {
        throw new Error("SHARED_HELD_ORDER_RESTORED_HELD_REPAIR_CHANGED");
      }
      await transaction.run(
        `INSERT INTO terminal_cart_fences (
          store_code, device_code, kind, hold_id, recall_attempt_id,
          bound_order_guid, created_at_iso
        ) VALUES (?, ?, 'RecallActive', ?, ?, NULL, ?)`,
        [
          claim.scope.storeCode,
          claim.scope.deviceCode,
          claim.holdGuid,
          claim.recallAttemptId,
          claim.createdAtIso,
        ],
      );
      return true;
    });
  }

  public async supersedeClaim(input: Readonly<{
    claimGuid: string;
    supersedeIdempotencyKey: string;
    supersededAtIso: string;
    expectedState: "Prepared" | "Active";
  }>): Promise<boolean> {
    return this.db.withExclusiveTransaction(async (transaction) => {
      const claim = await this.loadByGuid(transaction, input.claimGuid);
      if (!claim) return false;
      if (
        claim.state === "Superseded" &&
        claim.supersedeIdempotencyKey === input.supersedeIdempotencyKey
      ) {
        return true;
      }
      if (
        claim.state !== input.expectedState ||
        claim.supersedeIdempotencyKey !== null ||
        claim.releaseIdempotencyKey !== null ||
        claim.boundOrderGuid !== null ||
        (input.expectedState === "Prepared" &&
          claim.activateIdempotencyKey !== null) ||
        (input.expectedState === "Active" &&
          claim.activateIdempotencyKey === null)
      ) {
        return false;
      }
      await this.clearClaimFenceAndHeld(transaction, claim, input.supersededAtIso);
      const updated = await transaction.run(
        `UPDATE shared_held_order_claim_records
         SET state = 'Superseded',
             supersede_idempotency_key = ?,
             updated_at_iso = ?
         WHERE claim_guid = ?
           AND state = ?
           AND supersede_idempotency_key IS NULL
           AND release_idempotency_key IS NULL
           AND bound_order_guid IS NULL
           AND ((? = 'Prepared' AND activate_idempotency_key IS NULL)
             OR (? = 'Active' AND activate_idempotency_key IS NOT NULL))`,
        [
          input.supersedeIdempotencyKey,
          input.supersededAtIso,
          input.claimGuid,
          input.expectedState,
          input.expectedState,
          input.expectedState,
        ],
      );
      if (updated.changes !== 1) {
        throw new Error("SHARED_HELD_ORDER_CLAIM_SUPERSEDE_CHANGED");
      }
      return true;
    });
  }

  public async getClaim(
    claimGuid: string,
  ): Promise<SharedHeldOrderClaim | null> {
    return this.loadByGuid(this.db, claimGuid);
  }

  public async getOpenClaim(
    scope: HeldOrderScope,
  ): Promise<SharedHeldOrderClaim | null> {
    const claims = await this.listOpenClaims(scope);
    if (claims.length > 1) {
      throw new SharedHeldOrderClaimInvariantError();
    }
    return claims[0] ?? null;
  }

  public async listOpenClaims(
    scope: HeldOrderScope,
  ): Promise<readonly SharedHeldOrderClaim[]> {
    const rows = await this.db.getAll<ClaimRow>(
      `SELECT * FROM shared_held_order_claim_records
       WHERE store_code = ? AND device_code = ?
         AND state IN ('Prepared', 'Active')
       ORDER BY created_at_iso ASC, claim_guid ASC`,
      [scope.storeCode, scope.deviceCode],
    );
    return Promise.all(rows.map((row) => this.toClaim(row)));
  }

  public async getLatestClaimForHold(
    scope: HeldOrderScope,
    holdGuid: string,
  ): Promise<SharedHeldOrderClaim | null> {
    const row = await this.db.getFirst<ClaimRow>(
      `SELECT * FROM shared_held_order_claim_records
       WHERE store_code = ? AND device_code = ? AND hold_guid = ?
       ORDER BY created_at_iso DESC, claim_guid DESC
       LIMIT 1`,
      [scope.storeCode, scope.deviceCode, holdGuid],
    );
    return row ? this.toClaim(row) : null;
  }

  public async listMine(
    scope: HeldOrderScope,
    limit: number,
  ): Promise<readonly SharedHeldOrderClaim[]> {
    const safeLimit = listLimit(limit);
    const rows = await this.db.getAll<ClaimRow>(
      `SELECT * FROM shared_held_order_claim_records
       WHERE store_code = ? AND device_code = ?
       ORDER BY created_at_iso ASC, claim_guid ASC
       LIMIT ?`,
      [scope.storeCode, scope.deviceCode, safeLimit],
    );
    return Promise.all(rows.map((row) => this.toClaim(row)));
  }

  private async assertTerminalFenceAvailable(
    transaction: SqliteConnectionPort,
    facts: PreparedClaimInput,
  ): Promise<void> {
    const fence = await transaction.getFirst<{
      kind: string;
      hold_id: string;
      recall_attempt_id: string | null;
    }>(
      `SELECT kind, hold_id, recall_attempt_id
       FROM terminal_cart_fences
       WHERE store_code = ? AND device_code = ?`,
      [facts.scope.storeCode, facts.scope.deviceCode],
    );
    if (!fence) return;
    if (
      fence.kind === "RecallActive" &&
      fence.hold_id === facts.holdGuid &&
      fence.recall_attempt_id === facts.recallAttemptId
    ) {
      // 同 attempt 的 fence 必须已由同 facts 的 claim 建立；claim 单赢家由预检判定。
      const claim = await transaction.getFirst<{ claim_guid: string }>(
        `SELECT claim_guid FROM shared_held_order_claim_records
         WHERE hold_guid = ? AND recall_attempt_id = ?`,
        [facts.holdGuid, facts.recallAttemptId],
      );
      if (claim) return;
    }
    throw new Error("SHARED_HELD_ORDER_TERMINAL_FENCE_BUSY");
  }

  private async prepareLocalHeldRow(
    transaction: SqliteConnectionPort,
    facts: PreparedClaimInput,
    localHeldCiphertext: Uint8Array | null,
  ): Promise<void> {
    const summary = summarizeSharedSaleCartV1(facts.payload);
    const existing = await transaction.getFirst<{
      status: string;
      recall_attempt_id: string | null;
      is_synthetic_shared_claim: number;
    }>(
      `SELECT status, recall_attempt_id, is_synthetic_shared_claim
       FROM held_order_records WHERE hold_id = ?`,
      [facts.holdGuid],
    );
    if (facts.source === "RemoteClaim") {
      if (localHeldCiphertext === null) {
        throw new Error("SHARED_HELD_ORDER_LOCAL_PAYLOAD_MISSING");
      }
      // 远端 claim 必须使用 synthetic 行满足 fence FK/trigger；本机已有同 hold 则拒绝。
      if (existing) {
        throw new Error("SHARED_HELD_ORDER_CLAIM_LOCAL_HOLD_CONFLICT");
      }
      const localSequence = await allocateHeldLocalSequence(transaction);
      await transaction.run(
        `INSERT INTO held_order_records (
          hold_id, local_sequence, store_code, device_code,
          held_by_cashier_id, held_by_cashier_name, status, payload_version,
          payload_ciphertext, item_count, subtotal_cents, discount_cents,
          actual_amount_cents, recalling_at_iso, recall_attempt_id,
          recalling_cashier_id, recalling_cashier_name, recalled_at_iso,
          held_at_iso, created_at_iso, updated_at_iso, is_synthetic_shared_claim
        ) VALUES (?, ?, ?, ?, ?, ?, 'Recalling', 1, ?, ?, ?, ?, ?,
          ?, ?, ?, ?, NULL, ?, ?, ?, 1)`,
        [
          facts.holdGuid,
          localSequence,
          facts.scope.storeCode,
          facts.scope.deviceCode,
          facts.heldBy.cashierId,
          facts.heldBy.cashierName,
          localHeldCiphertext,
          summary.itemCount,
          summary.subtotalCents,
          summary.discountCents,
          summary.actualAmountCents,
          facts.createdAtIso,
          facts.recallAttemptId,
          facts.heldBy.cashierId,
          facts.heldBy.cashierName,
          facts.heldAtIso,
          facts.createdAtIso,
          facts.createdAtIso,
        ],
      );
      return;
    }
    // OfflineOrigin：必须引用真实本地副本，Pending 一次性推进到 Recalling。
    if (!existing) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_LOCAL_HOLD_MISSING");
    }
    if (
      existing.status === "Recalling" &&
      existing.recall_attempt_id === facts.recallAttemptId
    ) {
      return;
    }
    if (existing.status !== "Pending" || existing.recall_attempt_id !== null) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_LOCAL_HOLD_STATE_CONFLICT");
    }
    const changed = await transaction.run(
      `UPDATE held_order_records
       SET status = 'Recalling', recalling_at_iso = ?, recall_attempt_id = ?,
           recalling_cashier_id = ?, recalling_cashier_name = ?,
           updated_at_iso = ?
       WHERE hold_id = ? AND status = 'Pending' AND recall_attempt_id IS NULL`,
      [
        facts.createdAtIso,
        facts.recallAttemptId,
        facts.heldBy.cashierId,
        facts.heldBy.cashierName,
        facts.createdAtIso,
        facts.holdGuid,
      ],
    );
    if (changed.changes !== 1) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_LOCAL_HOLD_STATE_CONFLICT");
    }
  }

  private async finishRecallHeldAndFence(
    transaction: SqliteConnectionPort,
    claim: SharedHeldOrderClaim,
    atIso: string,
  ): Promise<void> {
    const changed = await transaction.run(
      `UPDATE held_order_records
       SET status = 'Recalled', recalled_at_iso = ?, updated_at_iso = ?
       WHERE hold_id = ? AND recall_attempt_id = ? AND status = 'Recalling'`,
      [atIso, atIso, claim.holdGuid, claim.recallAttemptId],
    );
    if (changed.changes !== 1) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_HELD_FINISH_MISSING");
    }
    const deleted = await transaction.run(
      `DELETE FROM terminal_cart_fences
       WHERE store_code = ? AND device_code = ?
         AND kind = 'RecallActive' AND hold_id = ? AND recall_attempt_id = ?
         AND bound_order_guid IS NULL`,
      [
        claim.scope.storeCode,
        claim.scope.deviceCode,
        claim.holdGuid,
        claim.recallAttemptId,
      ],
    );
    if (deleted.changes !== 1) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_FENCE_FINISH_MISSING");
    }
  }

  private async clearClaimFenceAndHeld(
    transaction: SqliteConnectionPort,
    claim: SharedHeldOrderClaim,
    atIso: string,
  ): Promise<void> {
    const deleted = await transaction.run(
      `DELETE FROM terminal_cart_fences
       WHERE store_code = ? AND device_code = ?
         AND kind = 'RecallActive' AND hold_id = ? AND recall_attempt_id = ?`,
      [
        claim.scope.storeCode,
        claim.scope.deviceCode,
        claim.holdGuid,
        claim.recallAttemptId,
      ],
    );
    if (deleted.changes !== 1) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_FENCE_CLEANUP_MISSING");
    }
    const held = await transaction.getFirst<{
      is_synthetic_shared_claim: number;
    }>(
      `SELECT is_synthetic_shared_claim FROM held_order_records
       WHERE hold_id = ?`,
      [claim.holdGuid],
    );
    if (!held) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_HELD_CLEANUP_MISSING");
    }
    if (held.is_synthetic_shared_claim === 1) {
      const removed = await transaction.run(
        `DELETE FROM held_order_records
         WHERE hold_id = ? AND recall_attempt_id = ?
           AND status = 'Recalling' AND is_synthetic_shared_claim = 1`,
        [claim.holdGuid, claim.recallAttemptId],
      );
      if (removed.changes !== 1) {
        throw new Error("SHARED_HELD_ORDER_CLAIM_HELD_CLEANUP_MISSING");
      }
      return;
    }
    const reset = await transaction.run(
      `UPDATE held_order_records
       SET status = 'Pending', recalling_at_iso = NULL, recall_attempt_id = NULL,
           recalling_cashier_id = NULL, recalling_cashier_name = NULL,
           updated_at_iso = ?
       WHERE hold_id = ? AND recall_attempt_id = ?
         AND status = 'Recalling' AND is_synthetic_shared_claim = 0`,
      [atIso, claim.holdGuid, claim.recallAttemptId],
    );
    if (reset.changes !== 1) {
      throw new Error("SHARED_HELD_ORDER_CLAIM_HELD_CLEANUP_MISSING");
    }
  }

  private async loadByGuid(
    db: SqliteConnectionPort,
    claimGuid: string,
  ): Promise<SharedHeldOrderClaim | null> {
    const row = await db.getFirst<ClaimRow>(
      "SELECT * FROM shared_held_order_claim_records WHERE claim_guid = ?",
      [claimGuid],
    );
    return row ? this.toClaim(row) : null;
  }

  private async loadByPrepareKey(
    db: SqliteConnectionPort,
    prepareIdempotencyKey: string,
  ): Promise<SharedHeldOrderClaim | null> {
    const row = await db.getFirst<ClaimRow>(
      "SELECT * FROM shared_held_order_claim_records WHERE prepare_idempotency_key = ?",
      [prepareIdempotencyKey],
    );
    return row ? this.toClaim(row) : null;
  }

  private async loadScopeFenceWinner(
    db: SqliteConnectionPort,
    scope: HeldOrderScope,
  ): Promise<SharedHeldOrderClaim | null> {
    const row = await db.getFirst<ClaimRow>(
      `SELECT * FROM shared_held_order_claim_records
       WHERE store_code = ? AND device_code = ?
         AND state IN ('Prepared', 'Active')
       ORDER BY created_at_iso ASC, claim_guid ASC
       LIMIT 1`,
      [scope.storeCode, scope.deviceCode],
    );
    return row ? this.toClaim(row) : null;
  }

  private async toClaim(row: ClaimRow): Promise<SharedHeldOrderClaim> {
    const plaintext = await this.encryptor.decrypt(row.payload_ciphertext);
    const payload = normalizeSharedSaleCartV1(
      JSON.parse(plaintext) as unknown,
    );
    return {
      claimGuid: row.claim_guid,
      holdGuid: row.hold_guid,
      recallAttemptId: row.recall_attempt_id,
      scope: { storeCode: row.store_code, deviceCode: row.device_code },
      source: row.source,
      state: row.state,
      prepareIdempotencyKey: row.prepare_idempotency_key,
      activateIdempotencyKey: row.activate_idempotency_key,
      releaseIdempotencyKey: row.release_idempotency_key,
      supersedeIdempotencyKey: row.supersede_idempotency_key,
      payload,
      serverRevision: row.server_revision,
      preparedExpiresAtIso: row.prepared_expires_at_iso,
      heldAtIso: row.held_at_iso,
      heldBy: {
        cashierId: row.held_by_cashier_id,
        cashierName: row.held_by_cashier_name,
      },
      boundOrderGuid: row.bound_order_guid,
      createdAtIso: row.created_at_iso,
      updatedAtIso: row.updated_at_iso,
    };
  }
}

function validatePreparedClaimInput(
  input: PreparedClaimInput,
): PreparedClaimInput {
  const claimGuid = nonBlank(input.claimGuid, "claim guid");
  const holdGuid = nonBlank(input.holdGuid, "hold guid");
  const recallAttemptId = nonBlank(input.recallAttemptId, "recall attempt id");
  const storeCode = nonBlank(input.scope.storeCode, "store code");
  const deviceCode = nonBlank(input.scope.deviceCode, "device code");
  const prepareIdempotencyKey = nonBlank(
    input.prepareIdempotencyKey,
    "prepare idempotency key",
  );
  if (input.source !== "RemoteClaim" && input.source !== "OfflineOrigin") {
    throw new TypeError("Invalid shared hold claim source.");
  }
  const payload = normalizeSharedSaleCartV1(input.payload);
  const preparedExpiresAtIso = canonicalIso(
    input.preparedExpiresAtIso,
    "prepared expiry",
  );
  const heldAtIso = canonicalIso(input.heldAtIso, "held at");
  const createdAtIso = canonicalIso(input.createdAtIso, "created at");
  const cashierId = nonBlank(input.heldBy.cashierId, "held by cashier id");
  const cashierName = nonBlank(
    input.heldBy.cashierName,
    "held by cashier name",
  );
  return Object.freeze({
    claimGuid,
    holdGuid,
    recallAttemptId,
    scope: Object.freeze({ storeCode, deviceCode }),
    source: input.source,
    prepareIdempotencyKey,
    payload,
    preparedExpiresAtIso,
    heldAtIso,
    heldBy: Object.freeze({ cashierId, cashierName }),
    createdAtIso,
  });
}

/** 同 prepare key 的幂等比对：任何 claim 事实不一致都拒绝重放。 */
function samePrepareFacts(
  claim: SharedHeldOrderClaim,
  facts: PreparedClaimInput,
): boolean {
  return (
    claim.claimGuid === facts.claimGuid &&
    claim.holdGuid === facts.holdGuid &&
    claim.recallAttemptId === facts.recallAttemptId &&
    claim.scope.storeCode === facts.scope.storeCode &&
    claim.scope.deviceCode === facts.scope.deviceCode &&
    claim.source === facts.source &&
    claim.preparedExpiresAtIso === facts.preparedExpiresAtIso &&
    claim.heldAtIso === facts.heldAtIso &&
    claim.heldBy.cashierId === facts.heldBy.cashierId &&
    claim.heldBy.cashierName === facts.heldBy.cashierName &&
    claim.createdAtIso === facts.createdAtIso &&
    deepEqual(claim.payload, facts.payload)
  );
}

/** synthetic held 行所需的汇总：与既有 summarizePricingState 口径一致。 */
function summarizeSharedSaleCartV1(cart: SharedSaleCartV1): Readonly<{
  itemCount: number;
  subtotalCents: number;
  discountCents: number;
  actualAmountCents: number;
}> {
  let subtotal = 0n;
  let discount = 0n;
  let actual = 0n;
  for (const line of cart.pricingState.lines) {
    // 与 frozen canonical validator 一致：decimal quantity 乘 cents 后逐行
    // MidpointRounding.AwayFromZero；gross 为非负数。
    const roundedGross = multiplyCentsAwayFromZero(
      line.quantity,
      line.unitPriceCents,
      "shared hold line gross",
    );
    if (roundedGross < 0) {
      throw new TypeError("Shared hold cart gross is invalid.");
    }
    const gross = BigInt(roundedGross);
    const lineDiscount = sharedLineDiscountCents(line.discountState, gross);
    subtotal += gross;
    discount += lineDiscount;
    actual += gross - lineDiscount;
  }
  return {
    // API summary 的 LineCount 语义可稳定容纳称重商品；SQLite item_count 为 INTEGER。
    itemCount: cart.pricingState.lines.length,
    subtotalCents: safeIntegerFromBigInt(subtotal, "held subtotal"),
    discountCents: safeIntegerFromBigInt(discount, "held discount"),
    actualAmountCents: safeIntegerFromBigInt(actual, "held actual amount"),
  };
}

function sharedLineDiscountCents(
  state: SharedSaleCartV1["pricingState"]["lines"][number]["discountState"],
  gross: bigint,
): bigint {
  switch (state.mode) {
    case "none":
      return 0n;
    case "manual-amount":
    case "promotion":
      return state.cents >= Number(gross)
        ? gross
        : BigInt(state.cents);
    case "manual-percent": {
      const numerator = gross * BigInt(state.basisPoints);
      const divisor = 10_000n;
      let quotient = numerator / divisor;
      if ((numerator % divisor) * 2n >= divisor) quotient += 1n;
      return quotient > gross ? gross : quotient;
    }
  }
}

async function allocateHeldLocalSequence(
  transaction: SqliteConnectionPort,
): Promise<number> {
  const row = await transaction.getFirst<{ next_sequence: number | string }>(
    "SELECT COALESCE(MAX(local_sequence), 0) + 1 AS next_sequence FROM held_order_records",
  );
  const value = typeof row?.next_sequence === "number"
    ? row.next_sequence
    : Number(row?.next_sequence ?? 1);
  if (!Number.isSafeInteger(value) || value < 1) {
    throw new Error("SHARED_HELD_ORDER_LOCAL_SEQUENCE_INVALID");
  }
  return value;
}

function listLimit(limit: number): number {
  if (!Number.isSafeInteger(limit) || limit < 1 || limit > 10_000) {
    throw new TypeError("Shared hold claim list limit is invalid.");
  }
  return limit;
}

function nonBlank(value: string, label: string): string {
  if (typeof value !== "string" || value.trim() === "" || value !== value.trim()) {
    throw new TypeError(`Invalid ${label}.`);
  }
  return value;
}

function canonicalIso(value: string, label: string): string {
  const raw = nonBlank(value, label);
  const milliseconds = Date.parse(raw);
  if (!Number.isFinite(milliseconds)) {
    throw new TypeError(`Invalid ${label}.`);
  }
  return new Date(milliseconds).toISOString();
}

function safeIntegerFromBigInt(value: bigint, label: string): number {
  if (
    value > BigInt(Number.MAX_SAFE_INTEGER) ||
    value < BigInt(Number.MIN_SAFE_INTEGER)
  ) {
    throw new RangeError(`${label} exceeds safe integer range.`);
  }
  return Number(value);
}

function deepEqual(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) return true;
  if (
    typeof left !== "object" ||
    typeof right !== "object" ||
    left === null ||
    right === null
  ) {
    return false;
  }
  if (Array.isArray(left) !== Array.isArray(right)) return false;
  if (Array.isArray(left)) {
    const rightArray = right as unknown[];
    if (left.length !== rightArray.length) return false;
    return left.every((value, index) => deepEqual(value, rightArray[index]));
  }
  const leftKeys = Object.keys(left as object).sort();
  const rightKeys = Object.keys(right as object).sort();
  if (
    leftKeys.length !== rightKeys.length ||
    leftKeys.some((key, index) => key !== rightKeys[index])
  ) {
    return false;
  }
  const leftRecord = left as Record<string, unknown>;
  const rightRecord = right as Record<string, unknown>;
  return leftKeys.every((key) => deepEqual(leftRecord[key], rightRecord[key]));
}
