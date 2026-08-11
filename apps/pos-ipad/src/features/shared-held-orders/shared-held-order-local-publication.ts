import {
  evaluateLegacyHeldOrderPayload,
} from "./legacy-held-order-evaluator";
import type { SharedPayloadEncryptorPort } from "./shared-held-order-claim-repository";
import type { SharedSaleCartV1 } from "./shared-sale-cart-v1";

import type { HeldOrderScope } from "@/core/contracts";
import type { SqliteConnectionPort } from "@/core/db/types";

export type LocalPublicationEligibility =
  | Readonly<{ eligible: true; cart: SharedSaleCartV1 }>
  | Readonly<{ eligible: false; reason: "not-found" | "not-shareable" | "in-progress" }>;

/**
 * 原设备离线 recall 的本地副本读取口：只读，不创建 fence、不改变挂单状态。
 * Pending/PendingPublish/Published 且 payload 可发布才允许 OfflineOrigin claim；
 * Recalling/Recalled（已被取走）或 payload 损坏一律拒绝，绝不覆盖远端事实。
 */
export interface SharedHeldOrderLocalPublicationPort {
  loadEligible(
    holdGuid: string,
    scope: HeldOrderScope,
  ): Promise<LocalPublicationEligibility>;
}

export class SqliteSharedHeldOrderLocalPublication
  implements SharedHeldOrderLocalPublicationPort
{
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly encryptor: SharedPayloadEncryptorPort,
  ) {}

  public async loadEligible(
    holdGuidInput: string,
    scope: HeldOrderScope,
  ): Promise<LocalPublicationEligibility> {
    const holdGuid = nonBlank(holdGuidInput, "hold guid");
    const storeCode = nonBlank(scope.storeCode, "store code");
    const deviceCode = nonBlank(scope.deviceCode, "device code");
    const row = await this.db.getFirst<{
      status: string;
      share_state: string;
      payload_version: number;
      payload_ciphertext: Uint8Array;
    }>(
      `SELECT status, share_state, payload_version, payload_ciphertext
       FROM held_order_records
       WHERE hold_id = ? AND store_code = ? AND device_code = ?`,
      [holdGuid, storeCode, deviceCode],
    );
    if (!row) return { eligible: false, reason: "not-found" };
    if (row.status === "Recalling" || row.status === "Recalled") {
      return { eligible: false, reason: "in-progress" };
    }
    if (
      row.status !== "Pending" ||
      !isShareableState(row.share_state) ||
      !(row.payload_ciphertext instanceof Uint8Array) ||
      row.payload_version !== 1
    ) {
      return { eligible: false, reason: "not-shareable" };
    }
    let plaintext: string;
    try {
      plaintext = await this.encryptor.decrypt(row.payload_ciphertext);
    } catch {
      return { eligible: false, reason: "not-shareable" };
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(plaintext) as unknown;
    } catch {
      return { eligible: false, reason: "not-shareable" };
    }
    const evaluation = evaluateLegacyHeldOrderPayload(parsed);
    if (evaluation.outcome !== "publishable") {
      return { eligible: false, reason: "not-shareable" };
    }
    return { eligible: true, cart: evaluation.cart };
  }
}

function isShareableState(state: string): boolean {
  return (
    state === "NeedsEvaluation" ||
    state === "PendingPublish" ||
    state === "Published"
  );
}

function nonBlank(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new TypeError(`${label} must not be blank.`);
  }
  return normalized;
}
