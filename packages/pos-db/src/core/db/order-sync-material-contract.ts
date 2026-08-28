export type OrderSyncMaterialErrorCode =
  | "ORDER_SYNC_ENVIRONMENT_INVALID"
  | "ORDER_SYNC_ORDER_MISMATCH"
  | "ORDER_SYNC_TENDER_MISMATCH"
  | "ORDER_SYNC_ATTEMPT_MISMATCH"
  | "ORDER_SYNC_RETURN_BINDING_MISMATCH"
  | "ORDER_SYNC_RETURN_CONTEXT_MISMATCH"
  | "ORDER_SYNC_LINE_PROVENANCE_MISSING"
  | "ORDER_SYNC_LINE_PROVENANCE_MISMATCH"
  | "ORDER_SYNC_CARD_EVIDENCE_MISMATCH"
  | "ORDER_SYNC_VOUCHER_STATE_MISMATCH"
  | "ORDER_SYNC_VOUCHER_REVERSAL_UNRESOLVED"
  | "ORDER_SYNC_VOUCHER_REVERSAL_MISMATCH"
  | "ORDER_SYNC_CARD_REVERSAL_UNSUPPORTED"
  | "ORDER_SYNC_HELD_SOURCE_MISSING";

/** 同步 wire 的不可变共享挂单来源；只从数据库解析，绝不依赖调用方内存对象。 */
export type ResolvedHeldOrderSource = Readonly<{
  holdGuid: string;
  claimGuid: string | null;
  sourceKind: 1 | 2;
}>;

export class OrderSyncMaterialError extends Error {
  public constructor(public readonly code: OrderSyncMaterialErrorCode) {
    super(`Order sync material was rejected (${code}).`);
    this.name = "OrderSyncMaterialError";
  }
}
