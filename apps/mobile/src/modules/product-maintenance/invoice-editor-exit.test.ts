import {
  resolveInvoiceEditorExitAction,
  resolveProductEditorStoreScope,
} from "./invoice-editor-exit";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  resolveInvoiceEditorExitAction({
    hasInvoiceReturnContext: true,
    hasUnsavedStorePrice: false,
    intent: "return",
  }),
  "return",
  "clean invoice editor returns immediately",
);

assertEqual(
  resolveInvoiceEditorExitAction({
    hasInvoiceReturnContext: true,
    hasUnsavedStorePrice: true,
    intent: "return",
  }),
  "confirm-discard",
  "dirty invoice editor asks before discarding",
);

assertEqual(
  resolveInvoiceEditorExitAction({
    hasInvoiceReturnContext: true,
    hasUnsavedStorePrice: true,
    intent: "save-and-return",
  }),
  "save-and-return",
  "dirty invoice editor saves before returning",
);

assertEqual(
  resolveInvoiceEditorExitAction({
    hasInvoiceReturnContext: false,
    hasUnsavedStorePrice: true,
    intent: "save-and-return",
  }),
  "stay",
  "ordinary product query has no invoice return action",
);

assertEqual(
  resolveProductEditorStoreScope({
    invoiceStoreCode: " KAW ",
    selectedStoreCode: "HQ",
    hasInvoiceReturnContext: true,
  }).storeCode,
  "KAW",
  "invoice editor uses the invoice store",
);

assertEqual(
  resolveProductEditorStoreScope({
    invoiceStoreCode: "KAW",
    selectedStoreCode: "HQ",
    hasInvoiceReturnContext: true,
  }).locked,
  true,
  "invoice editor store is locked",
);

assertEqual(
  resolveProductEditorStoreScope({
    invoiceStoreCode: "KAW",
    selectedStoreCode: "HQ",
    hasInvoiceReturnContext: false,
  }).storeCode,
  "HQ",
  "ordinary product query keeps the selected store",
);

assertEqual(
  resolveProductEditorStoreScope({
    invoiceStoreCode: " ",
    selectedStoreCode: " HQ ",
    hasInvoiceReturnContext: true,
  }).locked,
  false,
  "invalid invoice store does not create a false lock",
);
