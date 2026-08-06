export function parseCartQuantityInput(value: string): number | null {
  const normalizedValue = value.trim();

  if (!/^\d+$/.test(normalizedValue)) {
    return null;
  }

  const quantity = Number(normalizedValue);
  return Number.isSafeInteger(quantity) ? quantity : null;
}

export function shouldSubmitCartQuantityUpdate(currentQuantity: number, nextQuantity: number) {
  return currentQuantity !== nextQuantity;
}

interface CartQuantityItemIdentity {
  detailGUID?: string | null;
  productCode: string;
}

export function resolveCurrentCartQuantityItem<T extends CartQuantityItemIdentity>(
  items: readonly T[],
  editorItem: CartQuantityItemIdentity
): T | null {
  const detailGUID = editorItem.detailGUID?.trim();
  const detailMatch = detailGUID
    ? items.find((item) => item.detailGUID?.trim() === detailGUID)
    : undefined;
  if (detailMatch) {
    return detailMatch;
  }

  const productCode = editorItem.productCode.trim();
  return productCode
    ? items.find((item) => item.productCode.trim() === productCode) ?? null
    : null;
}

interface CartQuantityEditSubmitState {
  currentStoreCode?: string | null;
  editorStoreCode?: string | null;
  isPending?: boolean;
}

interface CartQuantityEditorDismissState {
  isPending?: boolean;
  isSubmitting?: boolean;
}

export function canDismissCartQuantityEditor({
  isPending = false,
  isSubmitting = false,
}: CartQuantityEditorDismissState) {
  return !isPending && !isSubmitting;
}

function normalizeCartQuantityStoreCode(value?: string | null) {
  const normalizedValue = value?.trim();
  return normalizedValue || null;
}

export function canSubmitCartQuantityEdit({
  currentStoreCode,
  editorStoreCode,
  isPending = false,
}: CartQuantityEditSubmitState) {
  // 编辑器记录打开时的门店，提交时必须仍是同一门店且没有请求在路上。
  return (
    !isPending &&
    normalizeCartQuantityStoreCode(editorStoreCode) !== null &&
    normalizeCartQuantityStoreCode(editorStoreCode) === normalizeCartQuantityStoreCode(currentStoreCode)
  );
}
