import type { SyncTargetStore } from "./types";

export interface SyncFieldSelection {
  syncRetailPrice: boolean;
  syncDiscountRate: boolean;
  syncPurchasePrice: boolean;
}

export const DEFAULT_SYNC_FIELDS: SyncFieldSelection = {
  syncRetailPrice: true,
  syncDiscountRate: true,
  syncPurchasePrice: false,
};

export function isSyncTargetSelectable(target: SyncTargetStore) {
  return target.hasRecord;
}

/**
 * 默认全选；特殊商品的分店价是店里刻意维护的，默认不选（仍可手动勾选）；
 * 没有该商品记录的分店无法同步，始终排除。
 */
export function getDefaultSelectedSyncTargets(targets: readonly SyncTargetStore[]): Set<string> {
  return new Set(
    targets
      .filter((target) => isSyncTargetSelectable(target) && !target.isSpecialProduct)
      .map((target) => target.storeCode)
  );
}

export function getSelectableSyncTargetCodes(targets: readonly SyncTargetStore[]): string[] {
  return targets.filter(isSyncTargetSelectable).map((target) => target.storeCode);
}

export type SyncTargetChange =
  | { kind: "noRecord" }
  | { kind: "same" }
  | { kind: "discountOnly" }
  | { kind: "price"; from: number | null; to: number | null };

function sameMoney(left: number | null, right: number | null) {
  return left != null && right != null && Math.round(left * 100) === Math.round(right * 100);
}

function sameRate(left: number | null, right: number | null) {
  return Math.abs((left ?? 0) - (right ?? 0)) <= 0.00005;
}

/** 每行右侧的「现价 → 新价 / 已一致 / 仅折扣变化」，只比较本次勾选要同步的字段。 */
export function describeSyncTargetChange(
  target: SyncTargetStore,
  source: { retailPrice: number | null; discountRate: number | null },
  fields: Pick<SyncFieldSelection, "syncRetailPrice" | "syncDiscountRate">
): SyncTargetChange {
  if (!target.hasRecord) {
    return { kind: "noRecord" };
  }
  const priceChanges = fields.syncRetailPrice && !sameMoney(target.retailPrice, source.retailPrice);
  const discountChanges = fields.syncDiscountRate && !sameRate(target.discountRate, source.discountRate);
  if (priceChanges) {
    return { kind: "price", from: target.retailPrice, to: source.retailPrice };
  }
  return discountChanges ? { kind: "discountOnly" } : { kind: "same" };
}
