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

interface CartQuantityEditSubmitState {
  currentStoreCode?: string | null;
  editorStoreCode?: string | null;
  isPending?: boolean;
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
