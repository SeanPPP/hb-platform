import type { PreorderActivationItem, PreorderDraftItemInput } from "./types";

export type PreorderPackCounts = Record<string, number>;

export function normalizePackCount(value: string | number) {
  const parsed = typeof value === "number" ? value : Number(value.trim());
  return Number.isFinite(parsed) ? Math.max(0, Math.trunc(parsed)) : 0;
}

export function createPackCounts(items: PreorderActivationItem[]): PreorderPackCounts {
  return Object.fromEntries(items.map((item) => [item.activationItemGuid, normalizePackCount(item.packCount)]));
}

export function buildDraftItems(
  items: PreorderActivationItem[],
  packCounts: PreorderPackCounts
): PreorderDraftItemInput[] {
  return items.map((item) => ({
    activationItemGuid: item.activationItemGuid,
    packCount: normalizePackCount(packCounts[item.activationItemGuid] ?? 0),
  }));
}

export function serializePackCounts(items: PreorderDraftItemInput[]) {
  return items.map((item) => `${item.activationItemGuid}:${item.packCount}`).join("|");
}

export function summarizePreorder(
  items: PreorderActivationItem[],
  packCounts: PreorderPackCounts
) {
  return items.reduce(
    (summary, item) => {
      const packCount = normalizePackCount(packCounts[item.activationItemGuid] ?? 0);
      const quantity = packCount * Math.max(1, item.minimumOrderQuantity);
      if (packCount > 0) summary.selectedSkuCount += 1;
      summary.totalPackCount += packCount;
      summary.totalQuantity += quantity;
      summary.totalImportAmount += quantity * item.importPrice;
      return summary;
    },
    { selectedSkuCount: 0, totalPackCount: 0, totalQuantity: 0, totalImportAmount: 0 }
  );
}
