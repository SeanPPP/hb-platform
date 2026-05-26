import {
  LOCAL_SUPPLIER_INVOICES_SOURCE,
  buildLocalSupplierInvoicesRestoreHref,
  buildLocalSupplierInvoicesReturnParams,
  decodeLocalSupplierInvoicesReturnParams,
} from "./navigation";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

const params = buildLocalSupplierInvoicesReturnParams({
  returnInvoiceGuid: "invoice-1",
  returnDetailsPage: 3,
  returnDetailsPageSize: 100,
  returnListPage: 2,
  returnListPageSize: 50,
  filters: {
    storeCode: " S01 ",
    supplierCode: "SUP01",
    invoiceNo: " INV-9 ",
    orderDateFrom: "2026-05-01",
    orderDateTo: "2026-05-18",
  },
  sort: {
    colId: "InvoiceNo",
    direction: "asc",
  },
});

assertEqual(
  params.source,
  LOCAL_SUPPLIER_INVOICES_SOURCE,
  "return params mark the local supplier invoices source"
);
assertEqual(params.returnListPage, "2", "return list page is encoded");
assertEqual(params.returnDetailsPageSize, "100", "return detail page size is encoded");
assertEqual(params.returnFilterStoreCode, "S01", "filter values are trimmed when encoded");
assertEqual(params.returnSortColId, "InvoiceNo", "sort column is encoded");

const decoded = decodeLocalSupplierInvoicesReturnParams({
  source: LOCAL_SUPPLIER_INVOICES_SOURCE,
  returnInvoiceGuid: "invoice-1",
  returnDetailsPage: "3",
  returnDetailsPageSize: "100",
  returnListPage: "2",
  returnListPageSize: "50",
  returnFilterStoreCode: "S01",
  returnFilterSupplierCode: "SUP01",
  returnFilterInvoiceNo: "INV-9",
  returnFilterOrderDateFrom: "2026-05-01",
  returnFilterOrderDateTo: "2026-05-18",
  returnSortColId: "InvoiceNo",
  returnSortDirection: "asc",
});

assertDeepEqual(
  decoded,
  {
    source: LOCAL_SUPPLIER_INVOICES_SOURCE,
    returnInvoiceGuid: "invoice-1",
    returnDetailsPage: 3,
    returnDetailsPageSize: 100,
    returnListPage: 2,
    returnListPageSize: 50,
    filters: {
      storeCode: "S01",
      supplierCode: "SUP01",
      invoiceNo: "INV-9",
      orderDateFrom: "2026-05-01",
      orderDateTo: "2026-05-18",
    },
    sort: {
      colId: "InvoiceNo",
      direction: "asc",
    },
  },
  "decode restores invoice state from route params"
);

assertEqual(
  decodeLocalSupplierInvoicesReturnParams({
    source: LOCAL_SUPPLIER_INVOICES_SOURCE,
    returnInvoiceGuid: "invoice-1",
    returnDetailsPageSize: "20",
  }),
  null,
  "decode rejects invalid detail page sizes"
);

assertEqual(
  decodeLocalSupplierInvoicesReturnParams({
    source: "other-screen",
    returnInvoiceGuid: "invoice-1",
  }),
  null,
  "decode ignores unrelated sources"
);

const restoreHref = buildLocalSupplierInvoicesRestoreHref({
  source: LOCAL_SUPPLIER_INVOICES_SOURCE,
  returnInvoiceGuid: "invoice-1",
  returnDetailsPage: 3,
  returnDetailsPageSize: 100,
  returnListPage: 2,
  returnListPageSize: 50,
  filters: {
    storeCode: "S01",
  },
  sort: {
    colId: "InvoiceNo",
    direction: "asc",
  },
});

assertEqual(
  restoreHref.pathname,
  "/(tabs)/local-supplier-invoices",
  "restore href targets the invoice screen"
);
assertEqual(
  restoreHref.params.returnInvoiceGuid,
  "invoice-1",
  "restore href includes encoded params"
);
