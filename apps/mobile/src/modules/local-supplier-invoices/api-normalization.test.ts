import {
  buildInvoiceDetailsGridRequest,
  buildInvoiceGridRequest,
  normalizeActiveLocalSuppliersResponse,
  normalizeInvoiceDetailsGridResponse,
  normalizeInvoiceGridResponse,
} from "./api";

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

const defaultListRequest = buildInvoiceGridRequest({});
assertEqual(defaultListRequest.startRow, 0, "default list starts from first row");
assertEqual(defaultListRequest.endRow, 20, "default list end row uses page size 20");
assertEqual(defaultListRequest.pageSize, 20, "default list page size is 20");
assertDeepEqual(
  defaultListRequest.sortModel,
  [{ colId: "OrderDate", sort: "desc" }],
  "default list sort is order date descending"
);

const filteredListRequest = buildInvoiceGridRequest({
  page: 2,
  pageSize: 50,
  filters: {
    storeCode: " S01 ",
    supplierCode: " SUP ",
    invoiceNo: " INV ",
    orderDateFrom: "2026-01-02",
    orderDateTo: "2026-01-05",
  },
  sort: { colId: "InvoiceNo", direction: "asc" },
});
assertEqual(filteredListRequest.startRow, 50, "second list page starts at row 50");
assertEqual(filteredListRequest.endRow, 100, "second list page ends at row 100");
assertEqual(filteredListRequest.pageSize, 50, "list page size accepts 50");
assertDeepEqual(
  filteredListRequest.filterModel,
  {
    storeCode: { filterType: "text", type: "contains", filter: "S01" },
    supplierCode: { filterType: "text", type: "contains", filter: "SUP" },
    invoiceNo: { filterType: "text", type: "contains", filter: "INV" },
    OrderDate: {
      filterType: "date",
      type: "inRange",
      filter: "2026-01-02",
      filterTo: "2026-01-05",
    },
  },
  "list filters trim text fields and send OrderDate range"
);

const blankDateRequest = buildInvoiceGridRequest({
  filters: {
    storeCode: " STO ",
    supplierCode: " LOC ",
    orderDateFrom: "   ",
    orderDateTo: "",
  },
});
assertDeepEqual(
  blankDateRequest.filterModel,
  {
    storeCode: { filterType: "text", type: "contains", filter: "STO" },
    supplierCode: { filterType: "text", type: "contains", filter: "LOC" },
  },
  "blank order date values do not send OrderDate while store and supplier filters remain correct"
);

const dateFromOnlyRequest = buildInvoiceGridRequest({
  filters: {
    orderDateFrom: "2026-02-01",
  },
});
assertDeepEqual(
  dateFromOnlyRequest.filterModel,
  {
    OrderDate: {
      filterType: "date",
      type: "greaterThanOrEqual",
      filter: "2026-02-01",
    },
  },
  "from-only order date uses greaterThanOrEqual grid filter"
);

const dateToOnlyRequest = buildInvoiceGridRequest({
  filters: {
    orderDateTo: "2026-02-28",
  },
});
assertDeepEqual(
  dateToOnlyRequest.filterModel,
  {
    OrderDate: {
      filterType: "date",
      type: "lessThanOrEqual",
      filter: "2026-02-28",
    },
  },
  "to-only order date uses lessThanOrEqual grid filter"
);

const invalidListRequest = buildInvoiceGridRequest({ page: -1, pageSize: 999 });
assertEqual(invalidListRequest.startRow, 0, "invalid list page falls back to first page");
assertEqual(invalidListRequest.pageSize, 20, "invalid list page size falls back to 20");

const detailsRequest = buildInvoiceDetailsGridRequest({ page: 3, pageSize: 100 });
assertEqual(detailsRequest.startRow, 200, "third detail page starts at row 200");
assertEqual(detailsRequest.endRow, 300, "third detail page ends at row 300");
assertEqual(detailsRequest.pageSize, 100, "detail page size accepts 100");

const invalidDetailsRequest = buildInvoiceDetailsGridRequest({ pageSize: 20 });
assertEqual(invalidDetailsRequest.pageSize, 50, "invalid detail page size falls back to 50");

const listPayload = normalizeInvoiceGridResponse({
  Items: [
    {
      InvoiceGUID: "invoice-1",
      StoreCode: "S01",
      StoreName: "Sydney",
      SupplierCode: "SUP01",
      SupplierName: "Supplier",
      InvoiceNo: "INV-1",
      OrderDate: "2026-01-05T00:00:00",
      TotalAmount: "12.50",
    },
  ],
  Total: "3",
});
assertEqual(listPayload.items.length, 1, "list payload keeps items");
assertEqual(listPayload.items[0]?.invoiceGuid, "invoice-1", "list normalizes invoice guid");
assertEqual(listPayload.items[0]?.totalAmount, 12.5, "list normalizes numeric amount");
assertEqual(listPayload.total, 3, "list normalizes total");

const detailsPayload = normalizeInvoiceDetailsGridResponse({
  items: [
    {
      detailGUID: "detail-1",
      productCode: "P001",
      itemNumber: "IT001",
      barcode: "BAR001",
      productName: "Tea",
      purchasePrice: "4.20",
      quantity: "6",
      productImage: "https://example.com/p.png",
    },
  ],
  total: "8",
});
assertEqual(detailsPayload.items.length, 1, "detail payload keeps items");
assertEqual(detailsPayload.items[0]?.detailGuid, "detail-1", "detail normalizes guid");
assertEqual(detailsPayload.items[0]?.purchasePrice, 4.2, "detail normalizes purchase price");
assertEqual(detailsPayload.items[0]?.quantity, 6, "detail normalizes quantity");
assertEqual(detailsPayload.total, 8, "detail normalizes total");

const activeSuppliersEnvelope = normalizeActiveLocalSuppliersResponse({
  success: true,
  data: [
    {
      LocalSupplierCode: "LOC-01",
      LocalSupplierName: "Local Supplier One",
    },
    {
      supplierCode: "LOC-02",
      Name: "Local Supplier Two",
    },
    {
      LocalSupplierName: "Missing Code",
    },
  ],
  message: "ok",
});
assertEqual(activeSuppliersEnvelope.length, 2, "active suppliers keep only rows with supplier code");
assertDeepEqual(
  activeSuppliersEnvelope,
  [
    {
      supplierCode: "LOC-01",
      supplierName: "Local Supplier One",
    },
    {
      supplierCode: "LOC-02",
      supplierName: "Local Supplier Two",
    },
  ],
  "active suppliers normalize LocalSupplier, camelCase, and backend Name fields from envelope data"
);

const activeSuppliersArray = normalizeActiveLocalSuppliersResponse([
  {
    LocalSupplierCode: "LOC-03",
    supplierName: "Mixed Case Supplier",
  },
]);
assertDeepEqual(
  activeSuppliersArray,
  [
    {
      supplierCode: "LOC-03",
      supplierName: "Mixed Case Supplier",
    },
  ],
  "active suppliers also normalize direct array payloads"
);
