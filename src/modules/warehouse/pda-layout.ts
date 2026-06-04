export type WarehouseProductLayoutMode = "pda" | "regular";
export type WarehouseLocationAction = "bind" | "unbind";
export type WarehouseLocationActionMode = "selection-modal" | "confirm-modal";
export type ProductLocationScanBindDecision = "bind" | "block" | "confirm";
export type ProductLocationLookupSource = "scan" | "manual";
export type ProductLocationLookupAction = ProductLocationScanBindDecision | "showResults";
export type WarehouseProductSummaryField =
  | "itemNumber"
  | "barcode"
  | "stockQuantity"
  | "location"
  | "domesticPrice"
  | "purchaseImportPrice"
  | "retailOemPrice"
  | "volume"
  | "middlePackageQuantity"
  | "packingQuantity"
  | "grade"
  | "warehouseStatus";

export interface WarehouseProductSectionConfig {
  showLocationAction: boolean;
  productSummaryColumns: 1 | 2;
  businessFieldsEditableInSummary: boolean;
  showStandaloneEditorCard: boolean;
}

const PDA_MAX_WIDTH = 390;
const PRODUCT_SUMMARY_ROWS: Record<WarehouseProductLayoutMode, WarehouseProductSummaryField[][]> = {
  pda: [
    ["itemNumber", "barcode"],
    ["stockQuantity", "location"],
    ["domesticPrice", "purchaseImportPrice", "retailOemPrice"],
    ["volume", "middlePackageQuantity", "packingQuantity"],
    ["grade", "warehouseStatus"],
  ],
  regular: [
    ["itemNumber", "barcode"],
    ["stockQuantity", "location"],
    ["purchaseImportPrice", "retailOemPrice"],
    ["domesticPrice", "middlePackageQuantity"],
    ["packingQuantity", "volume"],
    ["grade", "warehouseStatus"],
  ],
};

export function getWarehouseProductPdaLayout(width: number, options?: { forcePda?: boolean }): WarehouseProductLayoutMode {
  if (options?.forcePda) {
    return "pda";
  }
  return width <= PDA_MAX_WIDTH ? "pda" : "regular";
}

export function getWarehouseProductSections(mode: WarehouseProductLayoutMode): WarehouseProductSectionConfig {
  if (mode === "pda") {
    return {
      showLocationAction: true,
      productSummaryColumns: 2,
      businessFieldsEditableInSummary: true,
      showStandaloneEditorCard: false,
    };
  }

  return {
    showLocationAction: true,
    productSummaryColumns: 2,
    businessFieldsEditableInSummary: true,
    showStandaloneEditorCard: false,
  };
}

export function getWarehouseProductSummaryRows(mode: WarehouseProductLayoutMode) {
  return PRODUCT_SUMMARY_ROWS[mode];
}

export function getWarehouseProductSummaryVisualRows(mode: WarehouseProductLayoutMode) {
  return PRODUCT_SUMMARY_ROWS[mode].flatMap((group) => {
    const rows: WarehouseProductSummaryField[][] = [];
    for (let index = 0; index < group.length; index += 2) {
      rows.push(group.slice(index, index + 2));
    }
    return rows;
  });
}

export function getWarehouseLocationActionMode(action: WarehouseLocationAction): WarehouseLocationActionMode {
  return action === "unbind" ? "confirm-modal" : "selection-modal";
}

export function isPickWarehouseLocation(locationType?: number | null) {
  return locationType !== 2;
}

export function canBindMoreProductsToWarehouseLocation(locationType: number | null | undefined, productCount: number) {
  // 配货位按一对一商品处理；未知类型也按配货位收紧，避免误允许多商品绑定。
  return !isPickWarehouseLocation(locationType) || productCount <= 0;
}

export function getProductLocationScanBindDecision(
  locationType: number | null | undefined,
  productCount: number
): ProductLocationScanBindDecision {
  if (productCount <= 0) {
    return "bind";
  }
  return isPickWarehouseLocation(locationType) ? "block" : "confirm";
}

export function getProductLocationCandidateAction({
  locationType,
  productCount,
}: {
  locationType?: number | null;
  productCount: number;
}): ProductLocationScanBindDecision {
  return getProductLocationScanBindDecision(locationType, productCount);
}

export function isProductLocationCandidateDisabled(action: ProductLocationScanBindDecision, busy: boolean) {
  return busy || action === "block";
}

export function getProductLocationLookupAction({
  source,
  matchCount,
  locationType,
  productCount = 0,
}: {
  source: ProductLocationLookupSource;
  matchCount: number;
  locationType?: number | null;
  productCount?: number;
}): ProductLocationLookupAction {
  if (source !== "scan" || matchCount !== 1) {
    return "showResults";
  }
  return getProductLocationScanBindDecision(locationType, productCount);
}
