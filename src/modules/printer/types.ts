export type PrinterBarcodeKind = "EAN13" | "CODE128";

export interface SavedPrinter {
  name?: string | null;
  address: string;
}

export interface PrinterDevice {
  name?: string | null;
  address: string;
  bonded: boolean;
  connected: boolean;
}

export interface PrinterStatus {
  supported: boolean;
  enabled: boolean;
  connected: boolean;
  address?: string | null;
}

export interface PreparedBarcode {
  value: string;
  kind: PrinterBarcodeKind;
}

export interface ProductLabelPrintPayload {
  productName: string;
  itemNumber?: string | null;
  grade?: string | null;
  supplierName?: string | null;
  barcode?: string | null;
  retailPrice?: number | null;
  discountRate?: number | null;
  clearanceBarcode?: string | null;
  clearancePrice?: number | null;
}
