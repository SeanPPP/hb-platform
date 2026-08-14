import {
  SharedSaleCartValidationError,
  normalizeSharedSaleCartV1,
  toSharedSaleCartV1,
} from "./shared-sale-cart-v1";
import type {
  SharedSaleCartValidationCode,
} from "./shared-sale-cart-v1";
import {
  toSharedSaleCartV2,
  type SharedSaleCartPayload,
} from "./shared-sale-cart-v2";

import type { PricingCartStateSnapshot } from "@/core/contracts";

/**
 * 稳定阻断原因（机器码，非自由文本）：legacy evaluator 输出后由整合层
 * 写入 held_order_records.publish_block_reason。
 */
export type SharedHeldOrderBlockReason =
  | "LEGACY_PAYLOAD_CORRUPTED"
  | "LEGACY_PAYLOAD_VERSION_UNSUPPORTED"
  | SharedSaleCartValidationCode;

export type LegacyHeldOrderEvaluation =
  | Readonly<{ outcome: "publishable"; cart: SharedSaleCartPayload }>
  | Readonly<{ outcome: "blocked"; reason: SharedHeldOrderBlockReason }>;

/**
 * 评估既有 手持 POS 挂单 payload：有效普通 sale -> publishable（进入 PendingPublish），
 * 损坏/版本不支持/非普通 sale -> blocked + 稳定原因。
 */
export function evaluateLegacyHeldOrderPayload(
  input: unknown,
): LegacyHeldOrderEvaluation {
  if (!isRecord(input)) {
    return blocked("LEGACY_PAYLOAD_CORRUPTED");
  }
  if (input.version !== 1) {
    return blocked("LEGACY_PAYLOAD_VERSION_UNSUPPORTED");
  }
  if (!isRecord(input.pricingState)) {
    return blocked("LEGACY_PAYLOAD_CORRUPTED");
  }
  try {
    // 旧库密文是扁平 PricingCartStateSnapshot（kind/fixedPrice Money），
    // 先经显式映射器转成冻结 wire（pricingState 嵌套、mode 折扣、fixedPriceCents），
    // 再由 normalize 统一校验并给出稳定错误码。
    const snapshot = input.pricingState as unknown as PricingCartStateSnapshot;
    const hasCatalogBaseline = snapshot.lines.some(
      (line) => (line.catalogDiscountBasisPoints ?? 0) > 0,
    );
    const cart = hasCatalogBaseline
      ? toSharedSaleCartV2(snapshot)
      : normalizeSharedSaleCartV1(toSharedSaleCartV1(snapshot));
    return { outcome: "publishable", cart };
  } catch (error) {
    if (error instanceof SharedSaleCartValidationError) {
      return blocked(error.code);
    }
    return blocked("SHARED_CART_INVALID");
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function blocked(
  reason: SharedHeldOrderBlockReason,
): Readonly<{ outcome: "blocked"; reason: SharedHeldOrderBlockReason }> {
  return Object.freeze({ outcome: "blocked", reason });
}
