import type { ProductInsightRange } from "./types";

export function isValidProductInsightRange(
  range: ProductInsightRange,
): boolean {
  const validDate = (value: string) =>
    /^\d{4}-\d{2}-\d{2}$/.test(value) &&
    Number.isFinite(Date.parse(`${value}T00:00:00Z`)) &&
    new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
  return (
    validDate(range.startDate) &&
    validDate(range.endDate) &&
    range.startDate <= range.endDate
  );
}

export function canViewProductInsightBranches(
  isAuthenticated: boolean,
  hasPermission: (permission: string) => boolean,
  isReview: boolean,
) {
  return (
    isAuthenticated &&
    !isReview &&
    hasPermission("Reports.ProductMovement.View")
  );
}

export function findProductInsightStore<T extends { storeCode: string }>(
  stores: T[],
  requestedCode: string | null | undefined,
) {
  return (
    stores.find(
      (store) =>
        store.storeCode.toLowerCase() === requestedCode?.trim().toLowerCase(),
    ) ?? null
  );
}
