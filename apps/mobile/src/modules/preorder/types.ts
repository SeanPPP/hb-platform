export type PreorderActivationStatus = "Scheduled" | "Active" | "Closed" | "Cancelled" | string;
export type PreorderOrderStatus = "Draft" | "ReturnedForRevision" | "Submitted" | "NoDemand" | "Processing" | "Completed" | "Cancelled" | string;

export interface PreorderActivationSummary {
  activationGuid: string;
  templateGuid: string;
  templateName: string;
  periodNumber: number;
  activationCode: string;
  startAtUtc: string;
  endAtUtc: string;
  status: PreorderActivationStatus;
}

export interface ActivePreordersResult {
  storeCode: string;
  normalOrderBlocked: boolean;
  activations: PreorderActivationSummary[];
}

export interface PreorderActivationItem {
  activationItemGuid: string;
  productCode: string;
  itemNumber: string;
  productName: string;
  productImage?: string;
  importPrice: number;
  retailPrice: number;
  minimumOrderQuantity: number;
  packCount: number;
  orderedQuantity: number;
}

export interface PreorderActivationDetail extends PreorderActivationSummary {
  storeCode: string;
  draftRevision: number;
  orderGuid?: string;
  orderNo?: string;
  orderStatus?: PreorderOrderStatus;
  warehouseNotes?: string;
  items: PreorderActivationItem[];
}

export interface PreorderDraftItemInput {
  activationItemGuid: string;
  packCount: number;
}

export interface SavePreorderDraftInput {
  storeCode: string;
  expectedDraftRevision: number;
  items: PreorderDraftItemInput[];
}

export interface SubmitPreorderInput extends SavePreorderDraftInput {
  confirmNoDemand: boolean;
}

export interface PreorderSubmitResult {
  orderGuid: string;
  orderNo: string;
  status: string;
  draftRevision: number;
  submittedAt?: string;
  totalPackCount: number;
  totalQuantity: number;
  totalImportAmount: number;
  totalRetailAmount: number;
}
