export type InvoiceListPageSize = 20 | 50 | 100;
export type InvoiceDetailPageSize = 50 | 100 | 200;
export type SortDirection = "asc" | "desc";

export interface InvoiceGridFilters {
  storeCode?: string;
  supplierCode?: string;
  invoiceNo?: string;
  orderDateFrom?: string;
  orderDateTo?: string;
}

export interface LocalSupplierOption {
  supplierCode: string;
  supplierName: string;
}

export interface InvoiceGridSort {
  colId: "OrderDate" | "InvoiceNo" | "StoreName" | "SupplierName";
  direction: SortDirection;
}

export interface InvoiceGridQuery {
  page?: number;
  pageSize?: number;
  filters?: InvoiceGridFilters;
  sort?: InvoiceGridSort;
}

export interface InvoiceDetailsGridQuery {
  page?: number;
  pageSize?: number;
}

export interface GridFilterModel {
  filterType: "text" | "number" | "date" | "set";
  type: string;
  filter?: string;
  filterTo?: string;
  values?: string[];
}

export interface GridRequest {
  startRow: number;
  endRow: number;
  pageSize: number;
  filterModel?: Record<string, GridFilterModel>;
  sortModel?: Array<{ colId: string; sort: SortDirection }>;
}

export interface GridResult<T> {
  items: T[];
  total: number;
}

export interface LocalSupplierInvoice {
  invoiceGuid: string;
  storeCode: string;
  storeName: string;
  supplierCode: string;
  supplierName: string;
  invoiceNo: string;
  orderDate: string;
  inboundDate: string;
  totalAmount: number | null;
  receivedTotalAmount: number | null;
  flowStatus: number | null;
  inboundStatus: number | null;
  remarks: string;
  createdAt: string;
  updatedAt: string;
}

export interface LocalSupplierInvoiceItem {
  detailGuid: string;
  invoiceGuid: string;
  storeCode: string;
  supplierCode: string;
  productCode: string;
  storeProductCode: string;
  itemNumber: string;
  barcode: string;
  productName: string;
  specification: string;
  unit: string;
  quantity: number | null;
  purchasePrice: number | null;
  retailPrice: number | null;
  amount: number | null;
  productImage: string;
}
