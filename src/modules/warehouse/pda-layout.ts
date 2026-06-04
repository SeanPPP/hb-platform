export type WarehouseProductLayoutMode = "pda" | "regular";
export type WarehouseLocationAction = "bind" | "unbind";
export type WarehouseLocationActionMode = "selection-modal" | "confirm-modal";
export type ProductLocationScanBindDecision = "bind" | "block" | "confirm";
export type ProductLocationLookupSource = "scan" | "manual";
export type ProductLocationLookupAction = ProductLocationScanBindDecision | "showResults";

export interface WarehouseProductSectionConfig {
  showLocationAction: boolean;
  productSummaryColumns: 1 | 2;
  businessFieldsEditableInSummary: boolean;
  showStandaloneEditorCard: boolean;
}

const PDA_MAX_WIDTH = 390;

export function getWarehouseProductPdaLayout(width: number): WarehouseProductLayoutMode {
  return width <= PDA_MAX_WIDTH ? "pda" : "regular";
}

export function getWarehouseProductSections(mode: WarehouseProductLayoutMode): WarehouseProductSectionConfig {
  if (mode === "pda") {
    return {
      showLocationAction: true,
      productSummaryColumns: 1,
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
