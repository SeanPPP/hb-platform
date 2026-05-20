export enum ProductCreationType {
  Normal = 0,
  Set = 1,
  SetSubItem = 2,
}

export interface DomesticSupplierOption {
  supplierCode: string;
  supplierName: string;
}

export interface ProductPrefixOption {
  prefixCode: string;
  prefixName: string;
  prefixDescription?: string | null;
}

export interface DomesticProductBatch {
  batchNumber: string;
  supplierCode: string;
  supplierName?: string | null;
  prefixCode?: string | null;
  normalCount: number;
  setCount: number;
  totalCount: number;
  createdAt: string;
  createdBy?: string | null;
}

export interface DomesticProductBatchItem {
  itemNumber: string;
  hbProductNo: string;
  barcode: string;
  productName: string;
  productType: ProductCreationType;
  privateLabelPrice?: number | null;
  setQuantity?: number | null;
  setPrice?: number | null;
  parentItemNumber?: string | null;
}

export interface DomesticProductBatchDetail extends DomesticProductBatch {
  items: DomesticProductBatchItem[];
}

export interface CreateDomesticProductBatchRequest {
  supplierCode: string;
  prefixCode?: string;
  prefixName?: string;
  items: Array<{
    productName?: string;
    productType: ProductCreationType;
    privateLabelPrice?: number | null;
  }>;
}
