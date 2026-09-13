export type InvoiceEditorExitIntent = "return" | "save-and-return";

export type InvoiceEditorExitAction =
  | "stay"
  | "return"
  | "confirm-discard"
  | "save-and-return";

export function resolveInvoiceEditorExitAction({
  hasInvoiceReturnContext,
  hasUnsavedStorePrice,
  intent,
}: {
  hasInvoiceReturnContext: boolean;
  hasUnsavedStorePrice: boolean;
  intent: InvoiceEditorExitIntent;
}): InvoiceEditorExitAction {
  if (!hasInvoiceReturnContext) {
    return "stay";
  }

  if (intent === "save-and-return" && hasUnsavedStorePrice) {
    return "save-and-return";
  }

  if (intent === "return" && hasUnsavedStorePrice) {
    return "confirm-discard";
  }

  return "return";
}

function normalizeStoreCode(value?: string | null) {
  const normalized = value?.trim();
  return normalized || undefined;
}

export function resolveProductEditorStoreScope({
  invoiceStoreCode,
  selectedStoreCode,
  hasInvoiceReturnContext,
}: {
  invoiceStoreCode?: string | null;
  selectedStoreCode?: string | null;
  hasInvoiceReturnContext: boolean;
}) {
  const normalizedInvoiceStoreCode = normalizeStoreCode(invoiceStoreCode);
  if (hasInvoiceReturnContext && normalizedInvoiceStoreCode) {
    return {
      storeCode: normalizedInvoiceStoreCode,
      locked: true,
    } as const;
  }

  return {
    storeCode: normalizeStoreCode(selectedStoreCode),
    locked: false,
  } as const;
}
