import type {
  InvoiceDetailPageSize,
  InvoiceGridFilters,
  InvoiceGridSort,
  InvoiceListPageSize,
} from "./types";

export const LOCAL_SUPPLIER_INVOICES_SOURCE = "local-supplier-invoices";

const LIST_PAGE_SIZES: InvoiceListPageSize[] = [20, 50, 100];
const DETAIL_PAGE_SIZES: InvoiceDetailPageSize[] = [50, 100, 200];
const SORT_COLUMNS: InvoiceGridSort["colId"][] = [
  "OrderDate",
  "InvoiceNo",
  "StoreName",
  "SupplierName",
];
const SORT_DIRECTIONS: InvoiceGridSort["direction"][] = ["asc", "desc"];

type SearchParamValue = string | string[] | undefined;

export interface LocalSupplierInvoicesReturnState {
  source: typeof LOCAL_SUPPLIER_INVOICES_SOURCE;
  returnInvoiceGuid: string;
  returnDetailsPage: number;
  returnDetailsPageSize: InvoiceDetailPageSize;
  returnListPage: number;
  returnListPageSize: InvoiceListPageSize;
  filters: InvoiceGridFilters;
  sort: InvoiceGridSort;
}

export interface BuildLocalSupplierInvoicesReturnStateInput {
  returnInvoiceGuid: string;
  returnDetailsPage: number;
  returnDetailsPageSize: InvoiceDetailPageSize;
  returnListPage: number;
  returnListPageSize: InvoiceListPageSize;
  filters: InvoiceGridFilters;
  sort: InvoiceGridSort;
}

export interface LocalSupplierInvoicesRestoreHref {
  pathname: "/(tabs)/local-supplier-invoices";
  params: Record<string, string>;
}

function firstParam(value: SearchParamValue) {
  const raw = Array.isArray(value) ? value[0] : value;
  const trimmed = raw?.trim();
  return trimmed || undefined;
}

function normalizePositiveInteger(value: SearchParamValue) {
  const raw = firstParam(value);
  if (!raw) {
    return null;
  }

  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function normalizeListPageSize(value: SearchParamValue) {
  const parsed = normalizePositiveInteger(value);
  return parsed != null && LIST_PAGE_SIZES.includes(parsed as InvoiceListPageSize)
    ? parsed as InvoiceListPageSize
    : null;
}

function normalizeDetailPageSize(value: SearchParamValue) {
  const parsed = normalizePositiveInteger(value);
  return parsed != null && DETAIL_PAGE_SIZES.includes(parsed as InvoiceDetailPageSize)
    ? parsed as InvoiceDetailPageSize
    : null;
}

function normalizeSortColId(value: SearchParamValue) {
  const raw = firstParam(value);
  return raw && SORT_COLUMNS.includes(raw as InvoiceGridSort["colId"])
    ? raw as InvoiceGridSort["colId"]
    : null;
}

function normalizeSortDirection(value: SearchParamValue) {
  const raw = firstParam(value);
  return raw && SORT_DIRECTIONS.includes(raw as InvoiceGridSort["direction"])
    ? raw as InvoiceGridSort["direction"]
    : null;
}

function normalizeFilters(filters: InvoiceGridFilters): InvoiceGridFilters {
  return {
    storeCode: firstParam(filters.storeCode),
    supplierCode: firstParam(filters.supplierCode),
    invoiceNo: firstParam(filters.invoiceNo),
    orderDateFrom: firstParam(filters.orderDateFrom),
    orderDateTo: firstParam(filters.orderDateTo),
  };
}

export function buildLocalSupplierInvoicesReturnParams(
  input: BuildLocalSupplierInvoicesReturnStateInput
) {
  const filters = normalizeFilters(input.filters);

  return {
    source: LOCAL_SUPPLIER_INVOICES_SOURCE,
    returnInvoiceGuid: input.returnInvoiceGuid,
    returnDetailsPage: String(input.returnDetailsPage),
    returnDetailsPageSize: String(input.returnDetailsPageSize),
    returnListPage: String(input.returnListPage),
    returnListPageSize: String(input.returnListPageSize),
    returnFilterStoreCode: filters.storeCode ?? "",
    returnFilterSupplierCode: filters.supplierCode ?? "",
    returnFilterInvoiceNo: filters.invoiceNo ?? "",
    returnFilterOrderDateFrom: filters.orderDateFrom ?? "",
    returnFilterOrderDateTo: filters.orderDateTo ?? "",
    returnSortColId: input.sort.colId,
    returnSortDirection: input.sort.direction,
  };
}

export function decodeLocalSupplierInvoicesReturnParams(
  params: Record<string, SearchParamValue>
): LocalSupplierInvoicesReturnState | null {
  const source = firstParam(params.source);
  const returnInvoiceGuid = firstParam(params.returnInvoiceGuid);
  const returnDetailsPage = normalizePositiveInteger(params.returnDetailsPage);
  const returnDetailsPageSize = normalizeDetailPageSize(params.returnDetailsPageSize);
  const returnListPage = normalizePositiveInteger(params.returnListPage);
  const returnListPageSize = normalizeListPageSize(params.returnListPageSize);
  const sortColId = normalizeSortColId(params.returnSortColId);
  const sortDirection = normalizeSortDirection(params.returnSortDirection);

  if (
    source !== LOCAL_SUPPLIER_INVOICES_SOURCE
    || !returnInvoiceGuid
    || returnDetailsPage == null
    || returnDetailsPageSize == null
    || returnListPage == null
    || returnListPageSize == null
    || !sortColId
    || !sortDirection
  ) {
    return null;
  }

  return {
    source,
    returnInvoiceGuid,
    returnDetailsPage,
    returnDetailsPageSize,
    returnListPage,
    returnListPageSize,
    filters: normalizeFilters({
      storeCode: firstParam(params.returnFilterStoreCode),
      supplierCode: firstParam(params.returnFilterSupplierCode),
      invoiceNo: firstParam(params.returnFilterInvoiceNo),
      orderDateFrom: firstParam(params.returnFilterOrderDateFrom),
      orderDateTo: firstParam(params.returnFilterOrderDateTo),
    }),
    sort: {
      colId: sortColId,
      direction: sortDirection,
    },
  };
}

export function buildLocalSupplierInvoicesRestoreHref(
  state: LocalSupplierInvoicesReturnState
): LocalSupplierInvoicesRestoreHref {
  return {
    pathname: "/(tabs)/local-supplier-invoices",
    params: buildLocalSupplierInvoicesReturnParams(state),
  };
}
